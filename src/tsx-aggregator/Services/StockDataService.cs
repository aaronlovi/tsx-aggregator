using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
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
    private readonly ISearchService _searchService;
    private readonly RawCollector _rawCollector;

    public StockDataSvc(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StockDataSvc>>();
        _requestProcessor = svp.GetRequiredService<IStocksDataRequestsProcessor>();
        _quotesService = svp.GetRequiredService<IQuoteService>();
        _searchService = svp.GetRequiredService<ISearchService>();
        _rawCollector = svp.GetRequiredService<RawCollector>();
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

    public override async Task<GetStockSearchResultsReply> GetStockSearchResults(GetStockSearchResultsRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() { [LogUtils.ReqIdContext] = reqId });

        try {
            _logger.LogInformation("GetStockSearchResults");

            if (context is null) {
                _logger.LogWarning("GetStockSearchResults - Null context");
                return Failure("No context supplied");
            }

            _logger.LogInformation("GetStockSearchResults - Waiting for SearchService to be ready");
            await _searchService.SearchServiceReady.Task.WaitAsync(context.CancellationToken);
            _logger.LogInformation("GetStockSearchResults - SearchService is ready");

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new SearchServiceQuickSearchRequestInput(reqId, request.SearchTerm, cts);
            if (!_searchService.PostRequest(req)) {
                _logger.LogWarning("GetStockSearchResults - Failed to post request to search service, aborting");
                return Failure("Failed to post request");
            }

            object? response = await req.Completed.Task;
            if (response is not List<GetStockSearchResultsReplyItem> searchResults) {
                _logger.LogWarning("GetStockSearchResults - Received invalid response");
                return Failure("Got an invalid repsonse");
            }

            var reply = new GetStockSearchResultsReply() { Success = true };
            foreach (GetStockSearchResultsReplyItem item in searchResults) {
                reply.SearchResults.Add(item);
            }

            _logger.LogInformation("GetStockSearchResults - completed with {NumResults} results", searchResults.Count);

            return reply;

        } catch (OperationCanceledException) {
            _logger.LogWarning("GetStockSearchResults canceled");
            return Failure("GetStockSearchResults - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetStockSearchResults - General Fault");
            return Failure("GetStockSearchResults ran into a general error");
        }

        // Local helper methods
        static GetStockSearchResultsReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }

    public override async Task<GetStocksWithUpdatedRawDataReportsReply> GetStocksWithUpdatedRawDataReports(GetStocksWithUpdatedRawDataReportsRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId,
            [LogUtils.ExchangeContext] = request.Exchange,
        });

        try {
            _logger.LogInformation("GetStocksWithUpdatedRawDataReports({PageNumber},{PageSize})",
                request.PageNumber, request.PageSize);

            if (context is null) {
                _logger.LogWarning("GetStocksWithUpdatedRawDataReports - Null context");
                return Failure("No context supplied");
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new RawCollectorGetStocksWithUpdatedRawDataReportsRequestInput(
                reqId, request.Exchange, request.PageNumber, request.PageSize, cts);
            if (!_rawCollector.PostRequest(req)) {
                _logger.LogWarning("GetStocksWithUpdatedRawDataReports - Failed to post request to request processor, aborting");
                return Failure("Failed to post request");
            }

            var res_ = await req.Completed.Task;
            if (res_ is not Result<PagedInstrumentsWithRawDataReportUpdatesDto> res) {
                _logger.LogWarning("GetStocksWithUpdatedRawDataReports - Received invalid response");
                return Failure("Got an invalid repsonse");
            }

            if (!res.Success) {
                _logger.LogWarning("GetStocksWithUpdatedRawDataReports - Failed to get data from Raw Collector");
                return Failure("Failed to get data from Raw Collector");
            }

            GetStocksWithUpdatedRawDataReportsReply reply = res.Data!.ToGetStocksWithUpdatedRawDataReportsReply();

            _logger.LogInformation("GetStocksWithUpdatedRawDataReports - complete - {@Reply}", reply);
            return reply;
        } catch (OperationCanceledException) {
            _logger.LogWarning("GetStocksWithUpdatedRawDataReports canceled");
            return Failure("GetStocksWithUpdatedRawDataReports - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetStocksWithUpdatedRawDataReports - General Fault");
            return Failure("GetStocksWithUpdatedRawDataReports ran into a general error");
        }

        // Local helper methods

        static GetStocksWithUpdatedRawDataReportsReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }

    public override async Task<StockDataServiceReply> IgnoreRawDataReport(IgnoreRawDataReportRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId,
            [LogUtils.InstrumentIdContext] = request.InstrumentId
        });

        try {
            _logger.LogInformation("IgnoreRawDataReport");

            if (context is null) {
                _logger.LogWarning("IgnoreRawDataReport - Null context");
                return Failure("No context supplied");
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            Task cancellationTask = Utilities.CreateCancellationTask(cts.Token);

            var inputs = new RawCollectorIgnoreRawReportInput(
                reqId, request.InstrumentId, request.InstrumentReportIdToKeep, request.InstrumentReportIdsToIgnore, cts);
            _rawCollector.PostRequest(inputs);

            await Task.WhenAny(inputs.Completed.Task, cancellationTask);

            if (!inputs.Completed.Task.IsCompleted) {
                // The task was cancelled
                _logger.LogWarning("IgnoreRawDataReport - Canceled");
                return Failure("IgnoreRawDataReport - Canceled");
            }

            // The task completed in the Raw Collector service
            return HandleRawCollectorResponse(inputs);

        } catch (OperationCanceledException oex) {
            _logger.LogWarning(oex, "IgnoreRawDataReport - Canceled");
            return Failure("IgnoreRawDataReport - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "IgnoreRawDataReport - General Fault");
            return Failure("IgnoreRawDataReport ran into a general error");
        }

        // Local helper methods

        static StockDataServiceReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };

        StockDataServiceReply HandleRawCollectorResponse(RawCollectorIgnoreRawReportInput inputs) {
            if (inputs.Completed.Task.Result is not Result res) {
                _logger.LogWarning("IgnoreRawDataReport - Unexpected response from Raw Collector");
                return Failure("IgnoreRawDataReport - Unexpected response from Raw Collector");
            }

            if (res.Success)
                _logger.LogInformation("IgnoreRawDataReport - Success");
            else
                _logger.LogWarning("IgnoreRawDataReport - Failed: {ErrMsg}", res.ErrMsg);

            return new() { Success = res.Success, ErrorMessage = res.ErrMsg };
        }
    }
}
