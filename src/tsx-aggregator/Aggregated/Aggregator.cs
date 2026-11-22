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
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator;

public class Aggregator : BackgroundService, INamedService, IPausableService, IEventQueueWaker {
    private const string _serviceName = "Aggregator";
    private static readonly TimeSpan ShortInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongInterval = TimeSpan.FromMinutes(2);

    private long _reqId;
    private TimeSpan _intervalToNextTimeout;
    private readonly ILogger _logger;

    /// <summary>How long the loop waits after an unexpected error before reading the next input, so a repeating error cannot spin.</summary>
    internal TimeSpan ErrorBackoff { get; init; } = TimeSpan.FromSeconds(30);

    public DateTime? NextCycleTime { get; private set; }
    private readonly IDbmService _dbm;
    private readonly Channel<AggregatorInputBase> _inputChannel;
    private readonly AggregatorFsm _stateFsm;

    public Aggregator(IServiceProvider svp) {
        _reqId = 0;
        _intervalToNextTimeout = ShortInterval;
        _logger = svp.GetRequiredService<ILogger<Aggregator>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _inputChannel = Channel.CreateUnbounded<AggregatorInputBase>();
        _stateFsm = new(svp.GetRequiredService<ILogger<AggregatorFsm>>());

        _logger.LogInformation("Aggregator - Created");
    }

    public string ServiceName => _serviceName;

    public bool PostRequest(AggregatorInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    public bool IsPaused => _stateFsm.IsPaused;

    /// <summary>Checks the event queue now (through the FSM, so a paused Aggregator ignores it).</summary>
    public void CheckForEventsNow() => PostTimeoutRequest(DateTime.UtcNow);

    public Task SetPausedAsync(bool paused, CancellationToken ct) =>
        PausableService.SetPausedAsync(ServiceName, cts => new AggregatorPauseServiceInput(GetNextReqId(), pauseNotResume: paused, cts),
            PostRequest, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = Heartbeat.RunAsync(() => _logger.LogInformation("Aggregator heartbeat"), stoppingToken); // Fire and forget

        try {
            await RestorePausedState(stoppingToken);
            PostTimeoutRequest(DateTime.UtcNow); // Check for events right away, not after the first interval

            while (!stoppingToken.IsCancellationRequested) {
                var utcNow = DateTime.UtcNow;
                var nextTimeout = utcNow.Add(_intervalToNextTimeout);
                NextCycleTime = nextTimeout;
                TimeSpan interval = Utilities.CalculateTimeDifference(utcNow, nextTimeout);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(interval);
                var combinedToken = cts.Token; // Cancels after either the timeout interval

                AggregatorInputBase? input = null;
                try {
                    // Get the one next input, or timeout trying
                    input = await _inputChannel.Reader.ReadAsync(combinedToken);
                    _logger.LogInformation("Aggregator - Got a message {Input}", input);

                    using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                    using var thisRequestCts = CancellationTokenSource.CreateLinkedTokenSource(input.CancellationToken, stoppingToken);

                    var output = new AggregatorFsmOutputs();
                    _stateFsm.Update(input, output);
                    await ProcessOutput(input, output.OutputList, thisRequestCts.Token);

                    // A pause/resume that does not change the state produces no output, so complete it here.
                    if (input is AggregatorPauseServiceInput)
                        _ = input.Completed.TrySetResult(null);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    _logger.LogWarning("Aggregator - Application stop in progress, stopping");
                    break; // Process no more inputs
                } catch (OperationCanceledException) {
                    if (input is null) {
                        _logger.LogInformation("Aggregator - Interval elapsed, continuing");
                        PostTimeoutRequest(DateTime.UtcNow);
                    } else {
                        _logger.LogInformation("Aggregator - Request {ReqId} was cancelled by its caller", input.ReqId);
                        _ = input.Completed.TrySetCanceled();
                    }
                } catch (Exception ex) {
                    // One failed request must not stop the service: fail that request and carry on after a pause,
                    // so an error that repeats on every input cannot spin.
                    _logger.LogError(ex, "Aggregator - Request failed, continuing in {ErrorBackoff}", ErrorBackoff);
                    _ = input?.Completed.TrySetException(ex);
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
            }
        } catch (OperationCanceledException) {
            _logger.LogInformation("Aggregator - Cancellation encountered, main loop stopping");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error in Aggregator");
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

            _logger.LogInformation("Aggregator: Found instrument event {@InstrumentEvent}", instrumentEvt);
            bool handled = await ProcessInstrumentEvent(instrumentEvt, ct);
            _intervalToNextTimeout = ShortInterval;

            if (handled) {
                // Catch-up: there may be more events queued, so check again right away. The check goes through the
                // input channel and the FSM like any timeout, so pause requests and shutdown still get in between.
                PostTimeoutRequest(DateTime.UtcNow);
            } else {
                // The event is still unprocessed and would come straight back: retry after the short interval.
                _logger.LogInformation("Aggregator: Event not handled, retrying in {Interval}", ShortInterval);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessCheckForInstrumentEvent - unexpected error");
        }
    }

    /// <returns>True if the event was handled and marked processed, so it won't come back.</returns>
    internal async Task<bool> ProcessInstrumentEvent(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        _logger.LogInformation("Found instrument event: {InstrumentEvent}", instrumentEvt);

        CompanyEventTypes eventType = instrumentEvt.EventType.ToCompanyEventType();
        bool handled;
        switch (eventType) {
            case CompanyEventTypes.NewListedCompany:
            case CompanyEventTypes.UpdatedListedCompany:
            case CompanyEventTypes.ObsoletedListedCompany:
                // Nothing to aggregate for these yet; treat as handled.
                handled = true;
                break;
            case CompanyEventTypes.RawDataChanged:
                handled = await ProcessCompanyDataChangedEvent(instrumentEvt, ct);
                break;
            case CompanyEventTypes.Undefined:
            default:
                // Unknown event type: mark processed so it can't wedge the queue.
                _logger.LogWarning("ProcessInstrumentEvent - unknown event type {EventType}; marking processed", instrumentEvt.EventType);
                handled = true;
                break;
        }

        // Only mark the event processed when handling actually succeeded. A
        // transient failure (e.g. a DB error during aggregation) leaves it
        // unprocessed so the next cycle retries it, rather than silently
        // dropping the update.
        return handled && await MarkInstrumentEventAsProcessed(instrumentEvt, ct);
    }

    private async Task<bool> MarkInstrumentEventAsProcessed(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        InstrumentEventDto instrumentEventDto = instrumentEvt.InstrumentEvent with { IsProcessed = true };
        InstrumentEventExDto dto = instrumentEvt with { InstrumentEvent = instrumentEventDto };
        Result res = await _dbm.MarkInstrumentEventAsProcessed(dto, ct);
        if (!res.Success)
            _logger.LogWarning("MarkInstrumentEventAsProcessed - failed to mark instrument event as processed: {Error}", res.ErrMsg);
        return res.Success;
    }

    /// <returns>
    /// True if the event was handled (and may be marked processed); false on a
    /// transient failure that should be retried on the next cycle.
    /// </returns>
    private async Task<bool> ProcessCompanyDataChangedEvent(InstrumentEventExDto instrumentEvt, CancellationToken ct) {
        (Result res, IReadOnlyList<CurrentInstrumentRawDataReportDto> rawReports) = await _dbm.GetCurrentInstrumentReports(instrumentEvt.InstrumentId, ct);
        if (!res.Success) {
            _logger.LogWarning("ProcessCompanyDataChangedEvent - failed to read current reports, will retry: {Error}", res.ErrMsg);
            return false;
        }
        if (rawReports.Count == 0) {
            // No current raw data to aggregate. This is a data anomaly rather
            // than a transient error, so don't retry forever — mark it handled.
            _logger.LogWarning("ProcessCompanyDataChangedEvent - no current raw reports for instrument {InstrumentId}; nothing to aggregate", instrumentEvt.InstrumentId);
            return true;
        }

        var companyReportBuilder = new CompanyReportBuilder(
            instrumentEvt.InstrumentSymbol,
            instrumentEvt.InstrumentName,
            instrumentEvt.Exchange,
            instrumentEvt.NumShares,
            _logger);

        foreach (CurrentInstrumentRawDataReportDto rpt in rawReports)
            _ = companyReportBuilder.AddRawReport(rpt);

        CompanyReport companyReport = companyReportBuilder.Build();

        string serializedReport = JsonSerializer.Serialize(companyReport);
        var processedReportDto = new ProcessedInstrumentReportDto(instrumentEvt.InstrumentId, serializedReport, DateTimeOffset.UtcNow, null);
        res = await _dbm.InsertProcessedCompanyReport(processedReportDto, ct);
        if (!res.Success) {
            _logger.LogWarning("ProcessCompanyDataChangedEvent - failed to insert processed company report, will retry: {Error}", res.ErrMsg);
            return false;
        }

        return true;
    }

    private Task ProcessPersistCommonServiceState(AggregatorInputBase input, CancellationToken ct) =>
        PausableService.PersistPausedStateAsync(_dbm, _logger, ServiceName, _stateFsm.IsPaused, input, ct);

    /// <summary>Restores the paused state saved in service_state, so a pause survives a restart.</summary>
    private async Task RestorePausedState(CancellationToken ct) {
        (Result res, bool isPaused) = await _dbm.GetCommonServiceState(ServiceName, ct);
        if (!res.Success) {
            _logger.LogWarning("Aggregator - could not read the saved paused state, starting unpaused: {Error}", res.ErrMsg);
            return;
        }

        _stateFsm.IsPaused = isPaused;
        if (isPaused)
            _logger.LogWarning("Aggregator - starting paused (saved state)");
    }

    private long GetNextReqId() => Interlocked.Increment(ref _reqId);

    private void PostTimeoutRequest(DateTime nowUtc) {
        var reqId = GetNextReqId();
        var timeoutInput = new AggregatorTimeoutInput(reqId, null, nowUtc);
        _ = PostRequest(timeoutInput);
    }
}
