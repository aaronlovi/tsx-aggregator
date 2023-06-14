using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>
/// Responsible for processing data requests one-by-one
/// </summary>
/// <remarks>
/// TODO: Add checks for overloading the service
/// </remarks>
public class StocksDataRequestsProcessor : BackgroundService {
    private static readonly TimeSpan OneMinute = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Channel<StocksDataRequestsInputBase> _inputChannel;
    private readonly ChannelReader<StocksDataRequestsInputBase> _inputReader;
    private readonly ChannelWriter<StocksDataRequestsInputBase> _inputWriter;

    public StocksDataRequestsProcessor(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StocksDataRequestsProcessor>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<StocksDataRequestsInputBase>();
        _inputReader = _inputChannel.Reader;
        _inputWriter = _inputChannel.Writer;
    }

    public bool PostRequest(StocksDataRequestsInputBase inp) {
        return _inputWriter.TryWrite(inp);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        while (!stoppingToken.IsCancellationRequested) {
            await foreach (StocksDataRequestsInputBase inputBase in _inputReader.ReadAllAsync()) {
                using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = inputBase.ReqId });
                using var thisRequestCancelToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, inputBase.CancellationToken);
                switch (inputBase) {
                    case GetStocksForExchangeRequest getStocksForExchangeRequest: {
                        await ProcessGetStocksForExchangeRequest(getStocksForExchangeRequest, thisRequestCancelToken);
                        break;
                    }
                    default: {
                        _logger.LogError("StocksDataFetch main loop - Invalid request type received, dropping input");
                        break;
                    }
                }
            }
        }
    }

    private Task ProcessGetStocksForExchangeRequest(GetStocksForExchangeRequest request, CancellationTokenSource ct) {
        using var exchangeContext = _logger.BeginScope(new Dictionary<string, string> { [LogUtils.ExchangeContext] = request.Exchange });
        _logger.LogInformation("ProcessGetStocksForExchangeRequest");



        return;
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<StocksDataRequestsProcessor>>();
        while (!ct.IsCancellationRequested) {
            logger.LogInformation("StocksDataFetchingService heartbeat");
            await Task.Delay(OneMinute, ct);
        }
    }
}
