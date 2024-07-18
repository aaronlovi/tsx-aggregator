using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal partial class RawCollector : BackgroundService {
    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Registry _registry;
    private readonly StateFsm _stateFsm;
    private readonly HttpClient _httpClient;

    public RawCollector(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<RawCollector>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _registry = svp.GetRequiredService<Registry>();
        _stateFsm = new(DateTime.UtcNow, _registry);
        
        IHttpClientFactory httpClientFactory = svp.GetRequiredService<IHttpClientFactory>();
        _httpClient = CreateFetchDirectoryHttpClient(httpClientFactory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {

            await RestoreStateFsm(stoppingToken);
            await RestoreInstrumentDirectory(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                var utcNow = DateTime.UtcNow;
                _logger.LogInformation("Raw Worker running at: {time}", utcNow.ToLocalTime());

                var nextTimeout = _stateFsm.NextTimeout;
                var output = new StateFsmOutputs();

                TimeSpan interval = Utilities.CalculateTimeDifference(utcNow, nextTimeout);
                DateTime wakeupTime = utcNow.Add(interval);
                _logger.LogInformation("Sleeping until {WakeupTime}", wakeupTime.ToLocalTime());

                await Task.Delay(interval, stoppingToken);

                utcNow = DateTime.UtcNow;
                _stateFsm.Update(utcNow, output);
                await ProcessOutput(output.OutputList, stoppingToken);
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
        (Result res, StateFsmState? stateFsmState) = await _dbm.GetStateFsmState(ct);
        if (!res.Success) {
            _logger.LogError("RestoreStateFsm fatal error - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreStateFsm fatal error - " + res.ErrMsg);
        }

        if (stateFsmState is null) {
            _logger.LogWarning("RestoreStateFsm fatal error - could not restore state from database. New state created.");
            return;
        }

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

    private async Task ProcessOutput(IList<StateFsmOutputItemBase> outputList, CancellationToken ct) {
        foreach (var outputItem in outputList) {
            switch (outputItem) {
                case FetchDirectory: await ProcessFetchDirectory(ct); break;
                case FetchInstrumentData fid: await ProcessFetchInstrumentData(fid, ct); break;
                case PersistState: await ProcessPersistState(ct); break;
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
}
