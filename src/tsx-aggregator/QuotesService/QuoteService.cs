using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

public class QuoteService : BackgroundService, IQuoteService {
    private readonly ILogger _logger;
    private readonly Registry _registry;
    private readonly IDbmService _dbm;
    private readonly IGoogleSheetsService _sheetsService;
    private readonly IServiceProvider _svp;
    private readonly Channel<QuoteServiceInputBase> _inputChannel;
    private DateTime? _nextFetchQuotesTime;
    private readonly Dictionary<string, CachedPrice> _pricesByInstrumentSymbol; // In-memory cache of stock prices
    private int _consecutiveFailures;
    private DateTime? _retryAtUtc; // set after a failed cycle: when to try again

    private readonly record struct CachedPrice(decimal Price, DateTime SeenUtc);

    public QuoteService(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<QuoteService>>();
        _registry = svp.GetRequiredService<Registry>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _sheetsService = svp.GetRequiredService<IGoogleSheetsService>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<QuoteServiceInputBase>();
        _pricesByInstrumentSymbol = [];
        QuoteServiceReady = new();

        _logger.LogInformation("QuoteService - Created");
    }

    /// <summary>First wait after a failed fetch cycle; it doubles with each further failure, up to two hours.</summary>
    internal TimeSpan FailureRetryBase { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Wait between attempts to load the saved state at startup.</summary>
    internal TimeSpan StateLoadRetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>A cached price not seen in any read for this long is dropped.</summary>
    internal TimeSpan MaxPriceAge { get; init; } = TimeSpan.FromDays(1);

    public DateTime? NextFetchQuotesTime => _nextFetchQuotesTime;

    public TaskCompletionSource QuoteServiceReady { get; }

    public bool PostRequest(QuoteServiceInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = Heartbeat.RunAsync(() => _logger.LogInformation("QuoteService heartbeat - Next quotes fetch time {NextFetchQuotesTime}",
            DescribeNextFetchQuotesTime(_svp.GetRequiredService<IQuoteService>().NextFetchQuotesTime)), stoppingToken); // Fire and forget

        _logger.LogInformation("QuoteService - Entering main loop");

        // Wait for the registry to contain the list of stocks
        await _registry.DirectoryInitialized.Task.WaitAsync(stoppingToken);

        _logger.LogInformation("QuoteService - Directory initialized");

        ApplicationCommonState state = await LoadSavedState(stoppingToken);
        _nextFetchQuotesTime = state.NextFetchQuotesTime;
        _ = PostRequest(new QuoteServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));

        while (!stoppingToken.IsCancellationRequested) {

            await foreach (QuoteServiceInputBase input in _inputChannel.Reader.ReadAllAsync(stoppingToken)) {

                _logger.LogInformation("QuoteService - Got a message {Input}", input);

                using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                using var thisRequestCts = CancellationTokenSource.CreateLinkedTokenSource(input.CancellationToken, stoppingToken);

                switch (input) {
                    case QuoteServiceTimeoutInput ti: {
                        await ProcessQuoteServiceTimeoutInput(ti, thisRequestCts.Token, stoppingToken);
                        break;
                    }
                    case QuoteServiceFillPricesForSymbolsInput fillPricesInput:
                    {
                        ProcessQuoteServiceFillPricesForSymbolsInput(fillPricesInput);
                        break;
                    }
                    default: {
                        _logger.LogError("QuoteService main loop - Invalid request type received, dropping input");
                        break;
                    }
                }
            }
        }

        _logger.LogInformation("QuoteService - Exiting main loop");
    }

    /// <summary>
    /// Loads the saved next-fetch time, retrying until it works: without it the service never marks itself ready,
    /// and every price request would wait forever.
    /// </summary>
    private async Task<ApplicationCommonState> LoadSavedState(CancellationToken ct) {
        while (true) {
            (Result res, ApplicationCommonState? state) = await _dbm.GetApplicationCommonState(ct);
            if (res.Success && state is not null)
                return state;

            _logger.LogError("QuoteService - Failed to get state: {ErrMsg}, retrying in {Delay}", res.ErrMsg, StateLoadRetryDelay);
            await Task.Delay(StateLoadRetryDelay, ct);
        }
    }

    private async Task ProcessQuoteServiceTimeoutInput(QuoteServiceTimeoutInput ti, CancellationToken ct, CancellationToken stoppingToken) {
        _logger.LogInformation("ProcessQuoteServiceTimeoutInput {CurTime}", ti.CurTimeUtc);

        try {
            // Read the quotes already in the sheet: the starting prices after a restart, and a fallback in an outage
            Dictionary<string, decimal> oldPrices = await _sheetsService.ReadQuotes("A", "B", new string[] { "C" }, ct);
            if (oldPrices.Count == 0)
                _logger.LogWarning("Failed to read old quotes");

            _logger.LogInformation("Found {NumPrices} old quotes", oldPrices.Count);
            MergeIntoCache(oldPrices, DateTime.UtcNow);

            // Mark the Quote service as ready to accept price requests
            _ = QuoteServiceReady.TrySetResult();

            if (ti.CurTimeUtc < _nextFetchQuotesTime) {
                _logger.LogInformation("Not time to fetch new quotes yet, skipping");
                RecordSuccess();
                return;
            }

            // Clear the worksheet
            _sheetsService.ClearColumns();

            // Write the raw instrument symbols to the worksheet
            List<IList<object>> instrumentSymbols = GetInstrumentSymbols();
            _sheetsService.WriteRawValuesToRange("A", instrumentSymbols);

            // Write the formulas to the worksheet
            ConvertInstrumentSymbolsToGoogleFinanceForumula(instrumentSymbols);
            _sheetsService.WriteUserEnteredValuesToRange("B", instrumentSymbols);

            await _sheetsService.FetchQuoteOverrides(ct);

            // Get the new quotes. Cells Google has not computed yet are missing; those symbols keep their last price.
            Dictionary<string, decimal> newPrices = await _sheetsService.ReadQuotes("A", "B", new string[] { "C" }, ct);
            _logger.LogInformation("Found {NumPrices} new quotes", newPrices.Count);
            MergeIntoCache(newPrices, DateTime.UtcNow);

            // Only now that the quotes are in is the next fetch two hours away
            _nextFetchQuotesTime = ti.CurTimeUtc + Constants.TwoHours;
            Result saved = await _dbm.UpdateNextTimeToFetchQuotes(_nextFetchQuotesTime.Value, ct);
            if (!saved.Success)
                _logger.LogWarning("QuoteService - Failed to save the next fetch time: {ErrMsg}", saved.ErrMsg);

            RecordSuccess();
        } catch (OperationCanceledException) {
            _logger.LogInformation("ProcessQuoteServiceTimeoutInput - Operation cancelled");
        } catch (Exception ex) {
            TimeSpan retryIn = RecordFailure(DateTime.UtcNow);
            _logger.LogError(ex, "ProcessQuoteServiceTimeoutInput - general fault, retrying in {RetryIn}", retryIn);
        } finally {
            // The next timeout belongs to the service, not to this request, so it is cancelled only by shutdown
            SetupTimeoutTask(stoppingToken);
        }
    }

    /// <summary>
    /// Adds a read to the cache instead of replacing it, so a symbol missing from one read (a cell Google had not
    /// computed yet, or an empty read) keeps its last price. A price not seen for <see cref="MaxPriceAge"/> is dropped.
    /// </summary>
    private void MergeIntoCache(IReadOnlyDictionary<string, decimal> prices, DateTime nowUtc) {
        foreach ((string symbol, decimal price) in prices)
            _pricesByInstrumentSymbol[symbol] = new CachedPrice(price, nowUtc);

        List<string> stale = [];
        foreach ((string symbol, CachedPrice cached) in _pricesByInstrumentSymbol) {
            if (nowUtc - cached.SeenUtc > MaxPriceAge)
                stale.Add(symbol);
        }
        foreach (string symbol in stale)
            _ = _pricesByInstrumentSymbol.Remove(symbol);
        if (stale.Count > 0)
            _logger.LogInformation("QuoteService - Dropped {NumStale} prices not seen for {MaxPriceAge}", stale.Count, MaxPriceAge);
    }

    private void RecordSuccess() {
        _consecutiveFailures = 0;
        _retryAtUtc = null;
    }

    private TimeSpan RecordFailure(DateTime nowUtc) {
        _consecutiveFailures++;
        TimeSpan retryIn = RetryDelay(FailureRetryBase, _consecutiveFailures);
        _retryAtUtc = nowUtc + retryIn;
        return retryIn;
    }

    private List<IList<object>> GetInstrumentSymbols() {
        var retVal = new List<IList<object>>();

        IReadOnlyCollection<InstrumentDto> instruments = _registry.GetInstruments();
        foreach (InstrumentDto instrument in instruments) {
            retVal.Add([instrument.InstrumentSymbol]);
        }

        return retVal;
    }

    private static void ConvertInstrumentSymbolsToGoogleFinanceForumula(List<IList<object>> values) {
        for (int i = 0; i < values.Count; i++) {
            for (int j = 0; j < values[i].Count; j++) {
                values[i][j] = $"=GOOGLEFINANCE(\"TSE:{values[i][j].ToString() ?? string.Empty}\")";
            }
        }
    }

    private void SetupTimeoutTask(CancellationToken ct) {
        DateTime nowUtc = DateTime.UtcNow;
        TimeSpan timeUntilNextFetchQuotesTime = DelayUntilNextCheck(NextCheckTime(_nextFetchQuotesTime, _retryAtUtc, nowUtc), nowUtc);
        _ = Task.Delay(timeUntilNextFetchQuotesTime, ct).ContinueWith(t => {
            if (t.IsCanceled) {
                _logger.LogInformation("QuoteService - Timeout task cancelled");
                return;
            } else if (t.IsFaulted) {
                _logger.LogError("QuoteService - Timeout task faulted: {ErrMsg}", t.Exception?.Message);
                return;
            } else {
                _logger.LogInformation("QuoteService - Timeout reached, posting timeout message");
                _ = _inputChannel.Writer.TryWrite(new QuoteServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));
            }
        }, ct);
    }

    private void ProcessQuoteServiceFillPricesForSymbolsInput(QuoteServiceFillPricesForSymbolsInput inp)
    {
        _logger.LogInformation("ProcessQuoteServiceFillPricesForSymbolsInput {NumPricesToFill}", inp.Symbols.Count);

        var prices = new Dictionary<string, decimal>();

        try {
            // Fill in the prices
            foreach (string symbol in inp.Symbols) {
                if (_pricesByInstrumentSymbol.TryGetValue(symbol, out CachedPrice cached)) {
                    prices[symbol] = cached.Price;
                }
            }

            _ = inp.Completed.TrySetResult(prices);

        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessQuoteServiceFillPricesForSymbolsInput - general fault");
            _ = inp.Completed.TrySetException(ex);
        }
    }

    /// <summary>
    /// The next quote fetch time for the heartbeat log. It is null until the saved state is loaded (the first
    /// heartbeat comes before that), or for good if loading it failed.
    /// </summary>
    internal static string DescribeNextFetchQuotesTime(DateTime? nextFetchQuotesTimeUtc) =>
        nextFetchQuotesTimeUtc is DateTime t
            ? t.ToString("u", System.Globalization.CultureInfo.InvariantCulture)
            : "not known (saved state not loaded)";

    /// <summary>Longest single wait; a later fetch time is re-checked after this.</summary>
    internal static readonly TimeSpan MaxWaitBetweenChecks = TimeSpan.FromDays(1);

    /// <summary>
    /// How long to wait before the next timeout check: until the next fetch time, now if it is due or unknown, and
    /// at most <see cref="MaxWaitBetweenChecks"/>. Task.Delay throws beyond about 49.7 days, which stopped the host
    /// when the saved fetch time was far in the future.
    /// </summary>
    internal static TimeSpan DelayUntilNextCheck(DateTime? nextFetchQuotesTimeUtc, DateTime nowUtc) {
        TimeSpan delay = (nextFetchQuotesTimeUtc ?? nowUtc) - nowUtc;
        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;
        return delay > MaxWaitBetweenChecks ? MaxWaitBetweenChecks : delay;
    }

    /// <summary>
    /// When the timer should next fire: the next fetch time, unless a failed cycle set a retry time. A fetch that is
    /// due (or unknown) then waits for the retry, so a failure that repeats cannot spin.
    /// </summary>
    internal static DateTime? NextCheckTime(DateTime? nextFetchQuotesTimeUtc, DateTime? retryAtUtc, DateTime nowUtc) {
        if (retryAtUtc is null)
            return nextFetchQuotesTimeUtc;
        if (nextFetchQuotesTimeUtc is null || nextFetchQuotesTimeUtc <= nowUtc)
            return retryAtUtc;
        return nextFetchQuotesTimeUtc < retryAtUtc ? nextFetchQuotesTimeUtc : retryAtUtc;
    }

    /// <summary>Back-off after <paramref name="failures"/> failed cycles in a row: doubling, at most two hours.</summary>
    internal static TimeSpan RetryDelay(TimeSpan first, int failures) {
        int doublings = Math.Clamp(failures - 1, 0, 16);
        TimeSpan delay = first * Math.Pow(2, doublings);
        return delay > Constants.TwoHours ? Constants.TwoHours : delay;
    }
}
