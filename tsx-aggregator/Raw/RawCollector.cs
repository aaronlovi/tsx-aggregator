using System;
using System.Collections.Generic;
using System.Net.Http;
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

namespace tsx_aggregator;

internal partial class RawCollector : BackgroundService, INamedService {
    private const string _serviceName = "RawCollector";

    private long _reqId;
    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Registry _registry;
    private readonly RawCollectorFsm _stateFsm;
    private readonly Channel<RawCollectorInputBase> _inputChannel;
    private readonly HttpClient _httpClient;

    public RawCollector(IServiceProvider svp) {
        _reqId = 0;
        _logger = svp.GetRequiredService<ILogger<RawCollector>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _registry = svp.GetRequiredService<Registry>();
        _stateFsm = new(svp.GetRequiredService<ILogger<RawCollectorFsm>>(), DateTime.UtcNow, _registry);
        _inputChannel = Channel.CreateUnbounded<RawCollectorInputBase>();

        IHttpClientFactory httpClientFactory = svp.GetRequiredService<IHttpClientFactory>();
        _httpClient = CreateFetchDirectoryHttpClient(httpClientFactory);

        _logger.LogInformation("RawCollector - Created");
    }

    public string ServiceName => _serviceName;

    public bool PostRequest(RawCollectorInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await RestoreStateFsm(stoppingToken);
            await RestoreInstrumentDirectory(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                var utcNow = DateTime.UtcNow;
                var nextTimeout = _stateFsm.NextTimeout;
                TimeSpan interval = Utilities.CalculateTimeDifference(utcNow, nextTimeout);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(interval);
                var combinedToken = cts.Token; // Cancels after either the timeout interval

                DateTime wakeupTime = utcNow.Add(interval);
                _logger.LogInformation("RawCollector sleeping until {WakeupTime}", wakeupTime.ToLocalTime());

                try {
                    // Get the one next input, or timeout trying
                    RawCollectorInputBase input = await _inputChannel.Reader.ReadAsync(combinedToken);
                    _logger.LogInformation("RawCollector - Got a message {Input}", input);

                    using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                    using var thisRequestCts = Utilities.CreateLinkedTokenSource(input.CancellationTokenSource, stoppingToken);

                    await PreprocessInputs(input, thisRequestCts.Token);

                    var output = new RawCollectorFsmOutputs();
                    _stateFsm.Update(input, utcNow, output);
                    await ProcessOutput(input, output.OutputList, stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    _logger.LogWarning("RawCollector - Application stop in progress, stopping");
                    break; // Process no more inputs
                } catch (OperationCanceledException) {
                    _logger.LogInformation("RawCollector - Interval elapsed, continuing");
                    PostTimeoutRequest(DateTime.UtcNow);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Fatal error in RawCollector, exiting");
                    break; // Process no more inputs
                }

                // If the service is paused, then the next timeout could be in the past.
                // In this case, do not repeatedly smash the service and/or the database.
                // Log, and wait one minute.
                if (_stateFsm.IsPaused) {
                    _logger.LogInformation("RawCollector service is paused, waiting one minute");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) {
            _logger.LogInformation("RawCollector - Cancellation encountered, main loop stopping");
        }
        catch (Exception ex) {
            _logger.LogError("Error in RawCollector - {Error}", ex.Message);
        }
    }

    private async Task RestoreStateFsm(CancellationToken ct) {
        _logger.LogInformation("RestoreStateFsm");

        // Fetch specific service state
        (Result res, ApplicationCommonState? stateFsmState) = await _dbm.GetApplicationCommonState(ct);
        if (!res.Success) {
            _logger.LogError("RestoreStateFsm fatal error - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreStateFsm fatal error - " + res.ErrMsg);
        }

        // Fetch common service state
        (res, bool isPaused) = await _dbm.GetCommonServiceState(ServiceName, ct);
        if (!res.Success) {
            _logger.LogError("RestoreStateFsm fatal error getting common service state - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreStateFsm fatal error getting common service state - " + res.ErrMsg);
        }

        if (stateFsmState is null) {
            _logger.LogWarning("RestoreStateFsm fatal error - could not restore state from database. New state created.");
            return;
        }

        // Restore common service state
        stateFsmState.IsPaused = isPaused;

        _logger.LogInformation("RestoreStateFsm - restored state from database");
        _stateFsm.SetState(stateFsmState);
    }

    private async Task RestoreInstrumentDirectory(CancellationToken ct) {
        _logger.LogInformation("RestoreInstrumentDirectory");
        (Result res, IReadOnlyList<InstrumentDto> instrumentList) = await _dbm.GetInstrumentList(ct);

        if (!res.Success) {
            _logger.LogError("RestoreInstrumentDirectory fatal error - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreInstrumentDirectory fatal error - " + res.ErrMsg);
        }

        if (instrumentList.Count == 0) {
            _logger.LogWarning("RestoreInstrumentDirectory - no instruments to restore, starting from empty");
            return;
        }

        _registry.InitializeDirectory(instrumentList);
        _logger.LogInformation("RestoreInstrumentDirectory - restored {Count} instruments", instrumentList.Count);
    }

    private Task PreprocessInputs(RawCollectorInputBase inputs, CancellationToken ct) {
        if (inputs is RawCollectorIgnoreRawReportInput ignoreRawReportInput)
            return ProcessIgnoreRawReport(ignoreRawReportInput, ct);

        return Task.CompletedTask;
    }

    private async Task ProcessIgnoreRawReport(RawCollectorIgnoreRawReportInput inputs, CancellationToken ct) {
        var instrumentReportId = inputs.InstrumentReportId;
        var res = await _dbm.IgnoreRawUpdatedDataReport(instrumentReportId, ct);
        if (res.Success)
            _logger.LogInformation("PreprocessInputs - IgnoreRawUpdatedDataReport success");
        else
            _logger.LogWarning("PreprocessInputs - IgnoreRawUpdatedDataReport failed with error: {ErrMsg}", res.ErrMsg);

        inputs.Completed.TrySetResult(res);
    }

    private async Task ProcessOutput(
        RawCollectorInputBase input,
        IList<RawCollectorFsmOutputItemBase> outputList,
        CancellationToken ct) {
        foreach (var outputItem in outputList) {
            switch (outputItem) {
                case FetchRawCollectorDirectoryOutput:
                    await ProcessFetchDirectory(ct);
                    break;
                case FetchRawCollectorInstrumentDataOutput fid:
                    await ProcessFetchInstrumentData(fid, ct);
                    break;
                case PersistRawCollectorFsmState:
                    await ProcessPersistState(ct);
                    break;
                case PersistRawCollectorCommonServiceState:
                    await ProcessPersistCommonServiceState(input, ct);
                    break;
                default: {
                    _logger.LogWarning("ProcessOutput - Unexpected output type encountered: {@Output}", outputItem);
                    break;
                }
            }
        }
    }

    private async Task ProcessPersistState(CancellationToken ct) {
        _logger.LogInformation("ProcessPersistState begin");
        Result res = await _dbm.PersistStateFsmState(_stateFsm.GetCopyOfState(), ct);
        if (res.Success)
            _logger.LogInformation("ProcessPersistState success");
        else
            _logger.LogInformation("ProcessPersistState failed with error: {ErrMsg}", res.ErrMsg);
    }

    private async Task ProcessPersistCommonServiceState(RawCollectorInputBase input, CancellationToken ct) {
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
        var timeoutInput = new RawCollectorTimeoutInput(reqId, null, nowUtc);
        _ = PostRequest(timeoutInput);
    }
}
