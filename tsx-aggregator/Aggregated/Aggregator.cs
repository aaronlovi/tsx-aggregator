using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.Aggregated;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;
using Registry = tsx_aggregator.Raw.Registry;

namespace tsx_aggregator;

public class Aggregator : BackgroundService, INamedService {
    private const string _serviceName = "Aggregator";
    private static readonly TimeSpan ShortInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongInterval = TimeSpan.FromMinutes(2);

    private long _reqId;
    private TimeSpan _intervalToNextTimeout;
    private readonly ILogger _logger;
    private readonly IServiceProvider _svp;
    private readonly IDbmService _dbm;
    private readonly Channel<AggregatorInputBase> _inputChannel;
    private readonly AggregatorFsm _stateFsm;

    public Aggregator(IServiceProvider svp) {
        _reqId = 0;
        _intervalToNextTimeout = ShortInterval;
        _logger = svp.GetRequiredService<ILogger<Aggregator>>();
        _svp = svp;
        _dbm = svp.GetRequiredService<IDbmService>();
        _inputChannel = Channel.CreateUnbounded<AggregatorInputBase>();
        _stateFsm = new(svp.GetRequiredService<ILogger<AggregatorFsm>>());

        _logger.LogInformation("Aggregator - Created");
    }

    public string ServiceName => _serviceName;

    public bool PostRequest(AggregatorInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        try {
            while (!stoppingToken.IsCancellationRequested) {
                var utcNow = DateTime.UtcNow;
                var nextTimeout = utcNow.Add(_intervalToNextTimeout);
                TimeSpan interval = Utilities.CalculateTimeDifference(utcNow, nextTimeout);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(interval);
                var combinedToken = cts.Token; // Cancels after either the timeout interval

                try {
                    // Get the one next input, or timeout trying
                    AggregatorInputBase input = await _inputChannel.Reader.ReadAsync(combinedToken);
                    _logger.LogInformation("Aggregator - Got a message {Input}", input);

                    using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                    using var thisRequestCts = Utilities.CreateLinkedTokenSource(input.CancellationTokenSource, stoppingToken);

                    var output = new AggregatorFsmOutputs();
                    _stateFsm.Update(input, output);
                    await ProcessOutput(input, output.OutputList, thisRequestCts.Token);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    _logger.LogWarning("Aggregator - Application stop in progress, stopping");
                    break; // Process no more inputs
                } catch (OperationCanceledException) {
                    _logger.LogInformation("Aggregator - Interval elapsed, continuing");
                    PostTimeoutRequest(DateTime.UtcNow);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Fatal error in Aggregator, exiting");
                    break; // Process no more inputs
                }
            }
        } catch (OperationCanceledException) {
            _logger.LogInformation("Aggregator - Cancellation encountered, main loop stopping");
        } catch (Exception ex) {
            _logger.LogError("Error in Aggregator - {Error}", ex.Message);
        }
    }

    private async Task ProcessOutput(
        AggregatorInputBase input,
        IList<AggregatorFsmOutputItemBase> outputList,
        CancellationToken ct) {
        foreach (var outputItem in outputList) {
            switch (outputItem) {
                case ProcessCheckForInstrumentEventOutput:
                    await ProcessCheckForInstrumentEvent(ct);
                    break;
                case PersistAggregatorCommonServiceStateOutput:
                    await ProcessPersistCommonServiceState(input, ct);
                    break;
                default: {
                    _logger.LogWarning("ProcessOutput - Unexpected output type encountered: {@Output}", outputItem);
                    break;
                }
            }
        }
    }

    private async Task ProcessCheckForInstrumentEvent(CancellationToken ct) {
        try {
            (Result res, InstrumentEventExDto? instrumentEvt) = await _dbm.GetNextInstrumentEvent(ct);
            
            if (!res.Success) {
                // Do not hit the database too often if there is a problem.
                // So let aggregator wait the long interval before trying again
                _logger.LogWarning("Aggregator: Failed to get next instrument event: {Error}", res.ErrMsg);
                _intervalToNextTimeout = LongInterval;
                return;
            }

            if (instrumentEvt is null) {
                // No new instrument events in the database for the aggregator to work on
                // So let aggregator wait the long interval before checking for the next event
                _logger.LogInformation("Aggregator: No instrument events to process. Sleeping {Interval}",
                    LongInterval);
                _intervalToNextTimeout = LongInterval;
                return;
            }

            // So long as aggregator is picking up events, it may be in "catch up" mode.
            // So only wait the short interval before checking for the next event
            _logger.LogInformation("Aggregator: Found instrument event {@InstrumentEvent}", instrumentEvt);
            await ProcessInstrumentEvent(instrumentEvt, ct);
            _logger.LogInformation("Aggregator: Found instrument event. Sleeping only {Interval}", ShortInterval);
            _intervalToNextTimeout = ShortInterval;
        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessCheckForInstrumentEvent - unexpected error");
        }
    }

    private async Task ProcessInstrumentEvent(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        _logger.LogInformation("Found instrument event: {InstrumentEvent}", instrumentEvt);

        CompanyEventTypes eventType = instrumentEvt.EventType.ToCompanyEventType();
        switch (eventType) {
            case CompanyEventTypes.NewListedCompany:
                await ProcessNewListedCompanyEvent(instrumentEvt, ct);
                break;
            case CompanyEventTypes.UpdatedListedCompany:
                break;
            case CompanyEventTypes.ObsoletedListedCompany:
                break;
            case CompanyEventTypes.RawDataChanged:
                await ProcessCompanyDataChangedEvent(instrumentEvt, ct);
                break;
            case CompanyEventTypes.Undefined:
            default:
                break;
        }
    }

    private async Task ProcessNewListedCompanyEvent(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        // Mark the event as processed. Nothing to do here for now.
        InstrumentEventDto instrumentEventDto = instrumentEvt.InstrumentEvent with { IsProcessed = true };
        InstrumentEventExDto dto = instrumentEvt with { InstrumentEvent = instrumentEventDto };
        var res = await _dbm.MarkInstrumentEventAsProcessed(dto, ct);
        if (!res.Success)
            _logger.LogWarning("ProcessNewListedCompanyEvent - unexpected failed to mark instrument event as processed {Error}", res.ErrMsg);
    }

    private async Task ProcessCompanyDataChangedEvent(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        (Result res, IReadOnlyList<CurrentInstrumentRawDataReportDto> rawReports) = await _dbm.GetCurrentInstrumentReports(instrumentEvt.InstrumentId, ct);
        if (!res.Success || rawReports.Count == 0) {
            _logger.LogWarning("ProcessCompanyDataChangedEvent - unexpected no company data changed ({DbResult},{NumReports})",
                res.Success, rawReports.Count);
            return;
        }

        var companyReportBuilder = new CompanyReportBuilder(
            instrumentEvt.InstrumentSymbol,
            instrumentEvt.InstrumentName,
            instrumentEvt.Exchange,
            instrumentEvt.NumShares,
            _logger);

        foreach (CurrentInstrumentRawDataReportDto rpt in rawReports)
            companyReportBuilder.AddRawReport(rpt);

        CompanyReport companyReport = companyReportBuilder.Build();

        string serializedReport = JsonSerializer.Serialize(companyReport);
        var processedReportDto = new ProcessedInstrumentReportDto(instrumentEvt.InstrumentId, serializedReport, DateTimeOffset.UtcNow, null);
        res = await _dbm.InsertProcessedCompanyReport(processedReportDto, ct);
        if (!res.Success)
            _logger.LogWarning("ProcessCompanyDataChangedEvent - unexpected failed to insert processed company report: {Error}", res.ErrMsg);
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<Aggregator>>();
        while (!ct.IsCancellationRequested) {
            logger.LogInformation("Aggregator heartbeat");
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task ProcessPersistCommonServiceState(AggregatorInputBase input, CancellationToken ct) {
        _logger.LogInformation("ProcessPersistCommonServiceState begin");
        Result res = await _dbm.PersistCommonServiceState(_stateFsm.IsPaused, ServiceName, ct);
        if (res.Success) {
            _logger.LogInformation("ProcessPersistCommonServiceState success");

            // Indicate to any listeners that the pause/resume operation completed successfully
            input.Completed.TrySetResult(null);
        }
        else {
            _logger.LogInformation("ProcessPersistCommonServiceState failed with error: {ErrMsg}", res.ErrMsg);

            // Indicate to any listeners that the pause/resume operation failed
            input.Completed.TrySetException(new InvalidOperationException(res.ErrMsg));
        }
    }

    private long GetNextReqId() => Interlocked.Increment(ref _reqId);

    private void PostTimeoutRequest(DateTime nowUtc) {
        var reqId = GetNextReqId();
        var timeoutInput = new AggregatorTimeoutInput(reqId, null, nowUtc);
        _ = PostRequest(timeoutInput);
    }
}
