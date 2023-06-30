using System;
using System.Collections.Generic;
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
    private readonly IQuoteService _quotesService;

    public StockDataSvc(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StockDataSvc>>();
        _requestProcessor = svp.GetRequiredService<IStocksDataRequestsProcessor>();
        _quotesService = svp.GetRequiredService<IQuoteService>();
    }

    public override async Task<GetStocksDataReply> GetStocksData(GetStocksDataRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId, [LogUtils.ExchangeContext] = request.Exchange
        });

        try {
            _logger.LogInformation("GetStocksData");

            if (context is null) {
                _logger.LogWarning("GetStocksData - Null context");
                return Failure("No context supplied");
            }

            _logger.LogInformation("GetStocksData - Waiting for QuoteService to be ready");
            await _quotesService.QuoteServiceReady.Task.WaitAsync(context.CancellationToken);
            _logger.LogInformation("GetStocksData - QuoteService is ready");

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new GetStocksForExchangeRequest(reqId, request.Exchange, cts);
            if (!_requestProcessor.PostRequest(req)) {
                _logger.LogWarning("GetStocksData - Failed to post request to request processor, aborting");
                return Failure("Failed to post request");
            }

            object? response = await req.Completed.Task;
            if (response is not GetStocksDataReply reply) {
                _logger.LogWarning("GetStocksData - Received invalid response");
                return Failure("Got an invalid repsonse");
            }

            var symbols = new List<string>();
            foreach (GetStocksDataReplyItem replyItem in reply.StocksData) {
                symbols.Add(replyItem.InstrumentSymbol);
            }

            using CancellationTokenSource cts2 = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req2 = new QuoteServiceFillPricesForSymbolsInput(reqId, cts2, symbols);
            if (!_quotesService.PostRequest(req2)) {
                _logger.LogWarning("GetStocksData - Failed to post request to quotes service, aborting");
                return Failure("Failed to post request to quotes service");
            }

            object? response2 = await req2.Completed.Task;
            if (response2 is not IDictionary<string, decimal> prices) {
                _logger.LogWarning("GetStocksData - Received invalid response from quotes service");
                return Failure("Got an invalid repsonse from quotes service");
            }

            int numItemsMissingPrices = 0;
            for (int i = reply.StocksData.Count - 1; i >= 0; i--) {
                GetStocksDataReplyItem replyItem = reply.StocksData[i];
                if (prices.TryGetValue(replyItem.InstrumentSymbol, out decimal price)) {
                    replyItem.PerSharePrice = price;
                } else {
                    reply.StocksData.RemoveAt(i);
                    numItemsMissingPrices++;
                }
            }

            _logger.LogInformation("GetStocksData - complete. Num items missing prices {NumItemsMissingPrices}", numItemsMissingPrices);
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

    public override async Task<GetStocksDetailReply> GetStocksDetail(GetStocksDetailRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId,
            [LogUtils.ExchangeContext] = request.Exchange,
            [LogUtils.InstrumentSymbolContext] = request.InstrumentSymbol
        });

        try {
            _logger.LogInformation("GetStocksDetail");

            if (context is null) {
                _logger.LogWarning("GetStocksDetail - Null context");
                return Failure("No context supplied");
            }

            _logger.LogInformation("GetStocksDetail - Waiting for QuoteService to be ready");
            await _quotesService.QuoteServiceReady.Task.WaitAsync(context.CancellationToken);
            _logger.LogInformation("GetStocksDetail - QuoteService is ready");

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new GetStockDetailsForExchangeRequest(reqId, request.Exchange, request.InstrumentSymbol, cts);
            if (!_requestProcessor.PostRequest(req)) {
                _logger.LogWarning("GetStocksDetail - Failed to post request to request processor, aborting");
                return Failure("Failed to post request");
            }

            object? response = await req.Completed.Task;
            if (response is not GetStocksDetailReply reply) {
                _logger.LogWarning("GetStocksDetail - Received invalid response");
                return Failure("Got an invalid repsonse");
            }

            var symbols = new List<string>() { request.InstrumentSymbol };

            using CancellationTokenSource cts2 = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req2 = new QuoteServiceFillPricesForSymbolsInput(reqId, cts2, symbols);
            if (!_quotesService.PostRequest(req2)) {
                _logger.LogWarning("GetStocksDetail - Failed to post request to quotes service, aborting");
                return Failure("Failed to post request to quotes service");
            }

            object? response2 = await req2.Completed.Task;
            if (response2 is not IDictionary<string, decimal> prices) {
                _logger.LogWarning("GetStocksDetail - Received invalid response from quotes service");
                return Failure("Got an invalid repsonse from quotes service");
            }

            if (prices.TryGetValue(request.InstrumentSymbol, out decimal price)) {
                reply.StockDetail.PerSharePrice = price;
            } else {
                _logger.LogWarning("GetStocksDetail - Failed to get price");
            }

            _logger.LogInformation("GetStocksDetail - complete");
            return reply;

        } catch (OperationCanceledException) {
            _logger.LogWarning("GetStocksDetail canceled");
            return Failure("GetStocksDetail - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetStocksDetail - General Fault");
            return Failure("GetStocksDetail ran into a general error");
        }

        // Local helper methods
        static GetStocksDetailReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }
}
