using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

public class QuoteService : BackgroundService, IQuoteService {
    private const string CredentialKeyFileName = "linear-hangar-381814-2526009b7ea3.json";
    private const string GoogleApplicationName = "google sheets finance";
    private const string SpreadsheetId = "1QnzbL0I4AQCYSAZBiCyKUcbBI6HTlJkhglU5eW5Lk5Y";

    private readonly ILogger _logger;
    private readonly Registry _registry;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Channel<QuoteServiceInputBase> _inputChannel;
    private DateTime? _nextFetchQuotesTime;

    public QuoteService(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<QuoteService>>();
        _registry = svp.GetRequiredService<Registry>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<QuoteServiceInputBase>();

        _logger.LogInformation("QuoteService - Created");
    }

    public DateTime? NextFetchQuotesTime => _nextFetchQuotesTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        _logger.LogInformation("QuoteService - Entering main loop");

        // Wait for the registry to contain the list of stocks
        await _registry.DirectoryInitialized.Task;

        _logger.LogInformation("QuoteService - Directory initialized");

        (Result res, StateFsmState? state) = await _dbm.GetStateFsmState(stoppingToken);
        if (!res.Success || state is null) {
            _logger.LogError("QuoteService - Failed to get state: {ErrMsg}, aborting", res.ErrMsg);
            return;
        }

        _nextFetchQuotesTime = state.NextFetchQuotesTime;

        SetupTimeoutTask(stoppingToken);

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
        _logger.LogInformation("QuoteService - Processing QuoteServiceTimeoutInput {CurTime}", ti.CurTimeUtc);

        // Create the Google credential object
        var credential = GoogleCredential.FromFile(CredentialKeyFileName).CreateScoped(new[] { SheetsService.Scope.Spreadsheets });

        // Initialize the SheetsService instance
        var service = new SheetsService(new BaseClientService.Initializer() {
            HttpClientInitializer = credential,
            ApplicationName = GoogleApplicationName
        });

        // Write the raw instrument symbols to the worksheet
        List<IList<object>> instrumentSymbols = GetInstrumentSymbols();
        WriteToRange(service, column: "A", instrumentSymbols, SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW);

        // Write the formulas to the worksheet
        ConvertInstrumentSymbolsToGoogleFinanceForumula(instrumentSymbols);
        WriteToRange(service, column: "B", instrumentSymbols, SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED);

        // Update the next time to get stock quotes
        _nextFetchQuotesTime = ti.CurTimeUtc + Constants.TwoHours;
        await _dbm.UpdateNextTimeToFetchQuotes(_nextFetchQuotesTime.Value, ct);

        // Setup the next timeout task
        SetupTimeoutTask(ct);
    }

    private List<IList<object>> GetInstrumentSymbols() {
        List<IList<object>> retVal = new List<IList<object>>();

        IReadOnlyCollection<InstrumentDto> instruments = _registry.GetInstruments();
        foreach (InstrumentDto instrument in instruments) {
            retVal.Add(new List<object> { instrument.InstrumentSymbol });
        }

        return retVal;
    }

    private static void WriteToRange(SheetsService service, string column, List<IList<object>> values, SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum valueType) {

        // Set the range of cells to update
        string range = $"Sheet1!{column}1:{column}{values.Count}";

        // Create the ValueRange object
        var valueRange = new ValueRange { Values = values };

        // Execute the update request
        var request = service.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
        request.ValueInputOption = valueType;
        _ = request.Execute();
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
                _inputChannel.Writer.TryWrite(new QuoteServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));
            }
        }, ct);
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<QuoteService>>();
        IQuoteService quoteService = svp.GetRequiredService<IQuoteService>();
        DateTime nextFetchQuotesTime = quoteService.NextFetchQuotesTime ?? DateTime.MaxValue;
        while (!ct.IsCancellationRequested) {
            logger.LogInformation("QuoteService heartbeat - Next quotes fetch time {NextFetchQuotesTime}", nextFetchQuotesTime);
            await Task.Delay(Constants.OneMinute, ct);
        }
    }
}
