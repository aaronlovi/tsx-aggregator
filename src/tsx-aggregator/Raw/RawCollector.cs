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
using tsx_aggregator.Services;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal partial class RawCollector : BackgroundService, INamedService, IPausableService {
    private const string _serviceName = "RawCollector";

    /// <summary>The longest the loop sleeps before checking its timers again; waking early is harmless.</summary>
    private static readonly TimeSpan MaxWaitBetweenChecks = TimeSpan.FromDays(1);

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

    /// <summary>How long the loop waits after an unexpected error before reading the next input, so a repeating error cannot spin.</summary>
    internal TimeSpan ErrorBackoff { get; init; } = TimeSpan.FromSeconds(30);

    public string ServiceName => _serviceName;

    public bool PostRequest(RawCollectorInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    public bool IsPaused => _stateFsm.IsPaused;

    public async Task SetPausedAsync(bool paused, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var input = new RawCollectorPauseServiceInput(GetNextReqId(), pauseNotResume: paused, cts);
        if (!PostRequest(input))
            throw new InvalidOperationException("RawCollector - failed to post the pause/resume request");
        _ = await input.Completed.Task.WaitAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            // The instrument directory is loaded into the registry at startup by RegistryLoader.
            await RestoreStateFsm(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                var utcNow = DateTime.UtcNow;
                var nextTimeout = _stateFsm.NextTimeout;
                TimeSpan interval = Utilities.CalculateTimeDifference(utcNow, nextTimeout);
                if (interval > MaxWaitBetweenChecks)
                    interval = MaxWaitBetweenChecks; // CancelAfter throws beyond about 49.7 days

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(interval);
                var combinedToken = cts.Token; // Cancels after either the timeout interval

                DateTime wakeupTime = utcNow.Add(interval);
                _logger.LogInformation("RawCollector sleeping until {WakeupTime}", wakeupTime.ToLocalTime());

                RawCollectorInputBase? input = null;
                try {
                    // Get the one next input, or timeout trying
                    input = await _inputChannel.Reader.ReadAsync(combinedToken);
                    _logger.LogInformation("RawCollector - Got a message {Input}", input);

                    using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                    using var thisRequestCts = CancellationTokenSource.CreateLinkedTokenSource(input.CancellationToken, stoppingToken);

                    await PreprocessInputs(input, thisRequestCts.Token);

                    var output = new RawCollectorFsmOutputs();
                    _stateFsm.Update(input, utcNow, output);
                    await ProcessOutput(input, output.OutputList, stoppingToken);

                    // A pause/resume that does not change the state produces no output, so complete it here.
                    if (input is RawCollectorPauseServiceInput)
                        _ = input.Completed.TrySetResult(null);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    _logger.LogWarning("RawCollector - Application stop in progress, stopping");
                    break; // Process no more inputs
                } catch (OperationCanceledException) {
                    if (input is null) {
                        _logger.LogInformation("RawCollector - Interval elapsed, continuing");
                        PostTimeoutRequest(DateTime.UtcNow);
                    } else {
                        _logger.LogInformation("RawCollector - Request {ReqId} was cancelled by its caller", input.ReqId);
                        _ = input.Completed.TrySetCanceled();
                    }
                } catch (Exception ex) {
                    // One failed request must not stop scraping: fail that request and carry on after a pause,
                    // so an error that repeats on every input cannot spin.
                    _logger.LogError(ex, "RawCollector - Request failed, continuing in {ErrorBackoff}", ErrorBackoff);
                    _ = input?.Completed.TrySetException(ex);
                    await Task.Delay(ErrorBackoff, stoppingToken);
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
            _logger.LogError(ex, "Error in RawCollector");
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

    private Task PreprocessInputs(RawCollectorInputBase inputs, CancellationToken ct) {
        if (inputs is RawCollectorGetInstrumentsWithNoRawReportsInput noRawReportsInput)
            return ProcessGetInstrumentsWithNoRawReportsRequest(noRawReportsInput, ct);

        if (inputs is RawCollectorSetPriorityCompaniesInput setPriorityInput)
            return ProcessSetPriorityCompanies(setPriorityInput);

        if (inputs is RawCollectorGetPriorityCompaniesInput getPriorityInput)
            return ProcessGetPriorityCompanies(getPriorityInput);

        return Task.CompletedTask;
    }

    private Task ProcessSetPriorityCompanies(RawCollectorSetPriorityCompaniesInput input) {
        int validCount = _registry.SetPriorityCompanies(input.CompanySymbols);
        _logger.LogInformation("ProcessSetPriorityCompanies - set {ValidCount} valid priority companies out of {TotalCount} requested",
            validCount, input.CompanySymbols.Count);

        // Trigger immediate fetch by resetting the timer
        _stateFsm.NextFetchInstrumentDataTime = DateTime.UtcNow;

        _ = input.Completed.TrySetResult(validCount);
        return Task.CompletedTask;
    }

    private Task ProcessGetPriorityCompanies(RawCollectorGetPriorityCompaniesInput input) {
        var symbols = _registry.GetPriorityCompanySymbols();
        _ = input.Completed.TrySetResult(symbols);
        return Task.CompletedTask;
    }

    private async Task ProcessGetInstrumentsWithNoRawReportsRequest(
        RawCollectorGetInstrumentsWithNoRawReportsInput inputs, CancellationToken ct) {
        Result<PagedInstrumentInfoDto> res = await _dbm.GetInstrumentsWithNoRawReports(inputs.Exchange, inputs.PageNumber, inputs.PageSize, ct);
        if (res.Success)
            _logger.LogInformation("PreprocessInputs - GetInstrumentsWithNoRawReports success");
        else
            _logger.LogWarning("PreprocessInputs - GetInstrumentsWithNoRawReports failed with error: {ErrMsg}", res.ErrMsg);

        _ = inputs.Completed.TrySetResult(res);
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
            _ = input.Completed.TrySetResult(null);
        }
        else {
            _logger.LogInformation("ProcessPersistCommonServiceState failed with error: {ErrMsg}", res.ErrMsg);

            // Indicate to any listeners that the pause/resume operation failed
            _ = input.Completed.TrySetException(new InvalidOperationException(res.ErrMsg));
        }
    }

    private long GetNextReqId() => Interlocked.Increment(ref _reqId);

    private void PostTimeoutRequest(DateTime nowUtc) {
        var reqId = GetNextReqId();
        var timeoutInput = new RawCollectorTimeoutInput(reqId, null, nowUtc);
        _ = PostRequest(timeoutInput);
    }
}
