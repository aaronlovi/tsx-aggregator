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
    private Dictionary<string, decimal> _pricesByInstrumentSymbol; // In-memory cache of stock prices

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

    public DateTime? NextFetchQuotesTime => _nextFetchQuotesTime;

    public TaskCompletionSource QuoteServiceReady { get; }

    public bool PostRequest(QuoteServiceInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        _logger.LogInformation("QuoteService - Entering main loop");

        // Wait for the registry to contain the list of stocks
        await _registry.DirectoryInitialized.Task;

        _logger.LogInformation("QuoteService - Directory initialized");

        (Result res, ApplicationCommonState? state) = await _dbm.GetApplicationCommonState(stoppingToken);
        if (!res.Success || state is null) {
            _logger.LogError("QuoteService - Failed to get state: {ErrMsg}, aborting", res.ErrMsg);
            return;
        }

        _nextFetchQuotesTime = state.NextFetchQuotesTime;
        _ = PostRequest(new QuoteServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));

        while (!stoppingToken.IsCancellationRequested) {

            await foreach (QuoteServiceInputBase input in _inputChannel.Reader.ReadAllAsync(stoppingToken)) {

                _logger.LogInformation("QuoteService - Got a message {Input}", input);

                using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                using var thisRequestCts = Utilities.CreateLinkedTokenSource(input.CancellationTokenSource, stoppingToken);

                switch (input) {
                    case QuoteServiceTimeoutInput ti: {
                        await ProcessQuoteServiceTimeoutInput(ti, thisRequestCts.Token);
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

    private async Task ProcessQuoteServiceTimeoutInput(QuoteServiceTimeoutInput ti, CancellationToken ct) {
        _logger.LogInformation("ProcessQuoteServiceTimeoutInput {CurTime}", ti.CurTimeUtc);

        try {
            // Read the old quotes, in case there is an outage and we cannot get the current quotes
            Dictionary<string, decimal> oldPrices = await _sheetsService.ReadQuotes("A", "B", new string[] { "C" }, ct);
            if (oldPrices.Count == 0)
                _logger.LogWarning("Failed to read old quotes");

            _logger.LogInformation("Found {NumPrices} old quotes", oldPrices.Count);
            _pricesByInstrumentSymbol = oldPrices;

            // Mark the Quote service as ready to accept price requests
            _ = QuoteServiceReady.TrySetResult();

            if (ti.CurTimeUtc < _nextFetchQuotesTime) {
                _logger.LogInformation("Not time to fetch new quotes yet, skipping");
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

            // Update the next time to get stock quotes
            _nextFetchQuotesTime = ti.CurTimeUtc + Constants.TwoHours;
            _ = await _dbm.UpdateNextTimeToFetchQuotes(_nextFetchQuotesTime.Value, ct);

            await _sheetsService.FetchQuoteOverrides(ct);

            // Get the new quotes
            
            Dictionary<string, decimal> newPrices = await _sheetsService.ReadQuotes("A", "B", new string[] { "C" }, ct);

            _logger.LogInformation("Found {NumPrices} new quotes", newPrices.Count);
            _pricesByInstrumentSymbol = newPrices;

        } catch (OperationCanceledException) {
            _logger.LogInformation("ProcessQuoteServiceTimeoutInput - Operation cancelled");
        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessQuoteServiceTimeoutInput - general fault");
        } finally {
            // Setup the next timeout task
            SetupTimeoutTask(ct);
        }
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
        TimeSpan timeUntilNextFetchQuotesTime = (_nextFetchQuotesTime ?? nowUtc) - nowUtc;
        if (timeUntilNextFetchQuotesTime < TimeSpan.Zero)
            timeUntilNextFetchQuotesTime = TimeSpan.Zero;
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
                if (_pricesByInstrumentSymbol.TryGetValue(symbol, out decimal price)) {
                    prices[symbol] = price;
                }
            }

            _ = inp.Completed.TrySetResult(prices);

        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessQuoteServiceFillPricesForSymbolsInput - general fault");
            _ = inp.Completed.TrySetException(ex);
        }
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<QuoteService>>();
        IQuoteService quoteService = svp.GetRequiredService<IQuoteService>();
        while (!ct.IsCancellationRequested) {
            DateTime nextFetchQuotesTime = quoteService.NextFetchQuotesTime ?? DateTime.MaxValue;
            logger.LogInformation("QuoteService heartbeat - Next quotes fetch time {NextFetchQuotesTime}", nextFetchQuotesTime);
            await Task.Delay(Constants.OneMinute, ct);
        }
    }
}
