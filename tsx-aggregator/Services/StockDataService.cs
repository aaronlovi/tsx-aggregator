using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>
/// Responsible for serving requests from the outside world
/// </summary>
public class StockDataSvc : StockDataService.StockDataServiceBase {
    private long _reqId;
    private readonly ILogger _logger;
    private readonly IStocksDataRequestsProcessor _requestProcessor;

    public StockDataSvc(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StockDataSvc>>();
        _requestProcessor = svp.GetRequiredService<IStocksDataRequestsProcessor>();
    }

    public override async Task<GetStocksDataReply> GetStocksData(GetStocksDataRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var reqIdContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId, [LogUtils.ExchangeContext] = request.Exchange
        });

        try {
            _logger.LogInformation("GetStocksData");

            if (context is null) {
                _logger.LogWarning("GetStocksData - Null context");
                return Failure("No context supplied");
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using GetStocksForExchangeRequest req = new GetStocksForExchangeRequest(reqId, request.Exchange, cts);
            if (!_requestProcessor.PostRequest(req)) {
                _logger.LogWarning("GetStocksData - Failed to post request, aborting");
                return Failure("Failed to post request");
            }

            object? response = await req.Completed.Task;
            if (response is not GetStocksDataReply reply) {
                _logger.LogWarning("GetStocksData - Received invalid response");
                return Failure("Got an invalid repsonse");
            }

            _logger.LogInformation("GetStocksData - complete");
            return reply;

        } catch (OperationCanceledException) {
            _logger.LogWarning("GetStocksData canceled");
            return Failure("GetStocksData - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetStocksData - General Fault");
            return Failure("GetStocksData ran into a general error");
        }

        // Local helper methods
        static GetStocksDataReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }
}
