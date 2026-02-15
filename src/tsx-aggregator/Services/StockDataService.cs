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

            if (!reply.Success || reply.StockDetail is null) {
                _logger.LogWarning("GetStocksDetail - No data found for {InstrumentSymbol}", request.InstrumentSymbol);
                return reply;
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

    public override async Task<GetInstrumentsWithNoRawReportsReply> GetInstrumentsWithNoRawReports(
        GetInstrumentsWithNoRawReportsRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() {
            [LogUtils.ReqIdContext] = reqId,
            [LogUtils.ExchangeContext] = request.Exchange,
        });

        try {
            _logger.LogInformation("GetInstrumentsWithNoRawReports({PageNumber},{PageSize})",
                request.PageNumber, request.PageSize);

            if (context is null) {
                _logger.LogWarning("GetInstrumentsWithNoRawReports - Null context");
                return Failure("No context supplied");
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new RawCollectorGetInstrumentsWithNoRawReportsInput(
                reqId, request.Exchange, request.PageNumber, request.PageSize, cts);
            if (!_rawCollector.PostRequest(req)) {
                _logger.LogWarning("GetInstrumentsWithNoRawReports - Failed to post request, aborting");
                return Failure("Failed to post request");
            }

            var res_ = await req.Completed.Task;
            if (res_ is not Result<PagedInstrumentInfoDto> res) {
                _logger.LogWarning("GetInstrumentsWithNoRawReports - Received invalid response");
                return Failure("Got an invalid response");
            }

            if (!res.Success) {
                _logger.LogWarning("GetInstrumentsWithNoRawReports - Failed to get data");
                return Failure("Failed to get data");
            }

            GetInstrumentsWithNoRawReportsReply reply = res.Data!.ToGetInstrumentsWithNoRawReportsReply();
            _logger.LogInformation("GetInstrumentsWithNoRawReports - complete - {@Reply}", reply);
            return reply;
        } catch (OperationCanceledException) {
            _logger.LogWarning("GetInstrumentsWithNoRawReports canceled");
            return Failure("GetInstrumentsWithNoRawReports - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetInstrumentsWithNoRawReports - General Fault");
            return Failure("GetInstrumentsWithNoRawReports ran into a general error");
        }

        static GetInstrumentsWithNoRawReportsReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }

    public override async Task<StockDataServiceReply> SetPriorityCompanies(SetPriorityCompaniesRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() { [LogUtils.ReqIdContext] = reqId });

        try {
            _logger.LogInformation("SetPriorityCompanies - {Count} symbols", request.CompanySymbols.Count);

            if (context is null) {
                _logger.LogWarning("SetPriorityCompanies - Null context");
                return new StockDataServiceReply { Success = false, ErrorMessage = "No context supplied" };
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var symbols = new List<string>(request.CompanySymbols);
            using var input = new RawCollectorSetPriorityCompaniesInput(reqId, symbols, cts);
            if (!_rawCollector.PostRequest(input)) {
                _logger.LogWarning("SetPriorityCompanies - Failed to post request, aborting");
                return new StockDataServiceReply { Success = false, ErrorMessage = "Failed to post request" };
            }

            var result = await input.Completed.Task;
            _logger.LogInformation("SetPriorityCompanies - complete, {ValidCount} valid symbols", result);
            return new StockDataServiceReply { Success = true };
        } catch (OperationCanceledException) {
            _logger.LogWarning("SetPriorityCompanies canceled");
            return new StockDataServiceReply { Success = false, ErrorMessage = "SetPriorityCompanies - Canceled" };
        } catch (Exception ex) {
            _logger.LogError(ex, "SetPriorityCompanies - General Fault");
            return new StockDataServiceReply { Success = false, ErrorMessage = "SetPriorityCompanies ran into a general error" };
        }
    }

    public override async Task<GetPriorityCompaniesReply> GetPriorityCompanies(GetPriorityCompaniesRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() { [LogUtils.ReqIdContext] = reqId });

        try {
            _logger.LogInformation("GetPriorityCompanies");

            if (context is null) {
                _logger.LogWarning("GetPriorityCompanies - Null context");
                return new GetPriorityCompaniesReply { Success = false, ErrorMessage = "No context supplied" };
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var input = new RawCollectorGetPriorityCompaniesInput(reqId, cts);
            if (!_rawCollector.PostRequest(input)) {
                _logger.LogWarning("GetPriorityCompanies - Failed to post request, aborting");
                return new GetPriorityCompaniesReply { Success = false, ErrorMessage = "Failed to post request" };
            }

            var result = await input.Completed.Task;
            if (result is not IReadOnlyList<string> symbols) {
                _logger.LogWarning("GetPriorityCompanies - Received invalid response");
                return new GetPriorityCompaniesReply { Success = false, ErrorMessage = "Got an invalid response" };
            }

            var reply = new GetPriorityCompaniesReply { Success = true };
            foreach (var symbol in symbols)
                reply.CompanySymbols.Add(symbol);

            _logger.LogInformation("GetPriorityCompanies - complete, {Count} symbols in queue", symbols.Count);
            return reply;
        } catch (OperationCanceledException) {
            _logger.LogWarning("GetPriorityCompanies canceled");
            return new GetPriorityCompaniesReply { Success = false, ErrorMessage = "GetPriorityCompanies - Canceled" };
        } catch (Exception ex) {
            _logger.LogError(ex, "GetPriorityCompanies - General Fault");
            return new GetPriorityCompaniesReply { Success = false, ErrorMessage = "GetPriorityCompanies ran into a general error" };
        }
    }

    public override async Task<GetDashboardStatsReply> GetDashboardStats(GetDashboardStatsRequest request, ServerCallContext context) {
        long reqId = Interlocked.Increment(ref _reqId);
        using var logContext = _logger.BeginScope(new Dictionary<string, object>() { [LogUtils.ReqIdContext] = reqId });

        try {
            _logger.LogInformation("GetDashboardStats");

            if (context is null) {
                _logger.LogWarning("GetDashboardStats - Null context");
                return Failure("No context supplied");
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using var req = new GetDashboardStatsRequestInput(reqId, cts);
            if (!_requestProcessor.PostRequest(req)) {
                _logger.LogWarning("GetDashboardStats - Failed to post request, aborting");
                return Failure("Failed to post request");
            }

            object? response = await req.Completed.Task;
            if (response is not GetDashboardStatsReply reply) {
                _logger.LogWarning("GetDashboardStats - Received invalid response");
                return Failure("Got an invalid response");
            }

            _logger.LogInformation("GetDashboardStats - complete");
            return reply;

        } catch (OperationCanceledException) {
            _logger.LogWarning("GetDashboardStats canceled");
            return Failure("GetDashboardStats - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "GetDashboardStats - General Fault");
            return Failure("GetDashboardStats ran into a general error");
        }

        static GetDashboardStatsReply Failure(string errMsg) => new() { Success = false, ErrorMessage = errMsg };
    }

}
