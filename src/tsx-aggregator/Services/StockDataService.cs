using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    public override Task<GetStocksDataReply> GetStocksData(GetStocksDataRequest request, ServerCallContext context) {
        const string handler = nameof(GetStocksData);
        return HandleAsync(handler, context, errMsg => new GetStocksDataReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                await WaitUntilReadyAsync(handler, "QuoteService", _quotesService.QuoteServiceReady.Task, ct);

                GetStocksDataReply reply = await PostAndAwaitAsync<GetStocksForExchangeRequest, GetStocksDataReply>(handler,
                    cts => new(reqId, request.Exchange, cts), _requestProcessor.PostRequest, PostTexts.ToRequestProcessor, ct);

                IDictionary<string, decimal> prices = await GetPricesAsync(handler, reqId, StockPrices.InstrumentSymbols(reply), ct);
                int numItemsMissingPrices = StockPrices.FillAndDropUnpriced(reply, prices);

                _logger.LogInformation("GetStocksData - complete. Num items missing prices {NumItemsMissingPrices}", numItemsMissingPrices);
                return reply;
            },
            scope: [new(LogUtils.ExchangeContext, request.Exchange)]);
    }

    public override Task<GetStocksDetailReply> GetStocksDetail(GetStocksDetailRequest request, ServerCallContext context) {
        const string handler = nameof(GetStocksDetail);
        return HandleAsync(handler, context, errMsg => new GetStocksDetailReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                await WaitUntilReadyAsync(handler, "QuoteService", _quotesService.QuoteServiceReady.Task, ct);

                GetStocksDetailReply reply = await PostAndAwaitAsync<GetStockDetailsForExchangeRequest, GetStocksDetailReply>(handler,
                    cts => new(reqId, request.Exchange, request.InstrumentSymbol, cts), _requestProcessor.PostRequest,
                    PostTexts.ToRequestProcessor, ct);

                if (!reply.Success || reply.StockDetail is null) {
                    _logger.LogWarning("GetStocksDetail - No data found for {InstrumentSymbol}", request.InstrumentSymbol);
                    return reply;
                }

                // Unlike the list, the details keep a company without a price (at price 0).
                IDictionary<string, decimal> prices = await GetPricesAsync(handler, reqId, [request.InstrumentSymbol], ct);
                if (prices.TryGetValue(request.InstrumentSymbol, out decimal price)) {
                    reply.StockDetail.PerSharePrice = price;
                } else {
                    _logger.LogWarning("GetStocksDetail - Failed to get price");
                }

                _logger.LogInformation("GetStocksDetail - complete");
                return reply;
            },
            scope: [new(LogUtils.ExchangeContext, request.Exchange), new(LogUtils.InstrumentSymbolContext, request.InstrumentSymbol)]);
    }

    public override Task<GetStockSearchResultsReply> GetStockSearchResults(GetStockSearchResultsRequest request, ServerCallContext context) {
        const string handler = nameof(GetStockSearchResults);
        return HandleAsync(handler, context, errMsg => new GetStockSearchResultsReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                await WaitUntilReadyAsync(handler, "SearchService", _searchService.SearchServiceReady.Task, ct);

                List<GetStockSearchResultsReplyItem> searchResults =
                    await PostAndAwaitAsync<SearchServiceQuickSearchRequestInput, List<GetStockSearchResultsReplyItem>>(handler,
                        cts => new(reqId, request.SearchTerm, cts), _searchService.PostRequest, PostTexts.ToSearchService, ct);

                var reply = new GetStockSearchResultsReply() { Success = true };
                reply.SearchResults.AddRange(searchResults);

                _logger.LogInformation("GetStockSearchResults - completed with {NumResults} results", searchResults.Count);
                return reply;
            });
    }

    public override Task<GetInstrumentsWithNoRawReportsReply> GetInstrumentsWithNoRawReports(
        GetInstrumentsWithNoRawReportsRequest request, ServerCallContext context) {
        const string handler = nameof(GetInstrumentsWithNoRawReports);
        return HandleAsync(handler, context, errMsg => new GetInstrumentsWithNoRawReportsReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                Result<PagedInstrumentInfoDto> res = await PostAndAwaitAsync<RawCollectorGetInstrumentsWithNoRawReportsInput, Result<PagedInstrumentInfoDto>>(
                    handler, cts => new(reqId, request.Exchange, request.PageNumber, request.PageSize, cts), _rawCollector.PostRequest,
                    PostTexts.Plain, ct);

                if (!res.Success) {
                    _logger.LogWarning("GetInstrumentsWithNoRawReports - Failed to get data");
                    throw new HandlerFailedException("Failed to get data");
                }

                GetInstrumentsWithNoRawReportsReply reply = res.Data!.ToGetInstrumentsWithNoRawReportsReply();
                _logger.LogInformation("GetInstrumentsWithNoRawReports - complete - {@Reply}", reply);
                return reply;
            },
            logStart: () => _logger.LogInformation("GetInstrumentsWithNoRawReports({PageNumber},{PageSize})",
                request.PageNumber, request.PageSize),
            scope: [new(LogUtils.ExchangeContext, request.Exchange)]);
    }

    public override Task<StockDataServiceReply> SetPriorityCompanies(SetPriorityCompaniesRequest request, ServerCallContext context) {
        const string handler = nameof(SetPriorityCompanies);
        return HandleAsync(handler, context, errMsg => new StockDataServiceReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                var symbols = new List<string>(request.CompanySymbols);
                // The reply is the number of valid symbols, only logged, so its type is not checked.
                object? result = await PostAndAwaitAsync(handler, cts => new RawCollectorSetPriorityCompaniesInput(reqId, symbols, cts),
                    _rawCollector.PostRequest, PostTexts.Plain, ct);
                _logger.LogInformation("SetPriorityCompanies - complete, {ValidCount} valid symbols", result);
                return new StockDataServiceReply { Success = true };
            },
            logStart: () => _logger.LogInformation("SetPriorityCompanies - {Count} symbols", request.CompanySymbols.Count));
    }

    public override Task<GetPriorityCompaniesReply> GetPriorityCompanies(GetPriorityCompaniesRequest request, ServerCallContext context) {
        const string handler = nameof(GetPriorityCompanies);
        return HandleAsync(handler, context, errMsg => new GetPriorityCompaniesReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                IReadOnlyList<string> symbols = await PostAndAwaitAsync<RawCollectorGetPriorityCompaniesInput, IReadOnlyList<string>>(handler,
                    cts => new(reqId, cts), _rawCollector.PostRequest, PostTexts.Plain, ct);

                var reply = new GetPriorityCompaniesReply { Success = true };
                reply.CompanySymbols.AddRange(symbols);

                _logger.LogInformation("GetPriorityCompanies - complete, {Count} symbols in queue", symbols.Count);
                return reply;
            });
    }

    public override Task<GetDashboardStatsReply> GetDashboardStats(GetDashboardStatsRequest request, ServerCallContext context) {
        const string handler = nameof(GetDashboardStats);
        return HandleAsync(handler, context, errMsg => new GetDashboardStatsReply { Success = false, ErrorMessage = errMsg },
            async (reqId, ct) => {
                GetDashboardStatsReply reply = await PostAndAwaitAsync<GetDashboardStatsRequestInput, GetDashboardStatsReply>(handler,
                    cts => new(reqId, cts), _requestProcessor.PostRequest, PostTexts.Plain, ct);

                _logger.LogInformation("GetDashboardStats - complete");
                return reply;
            });
    }

    // ---- The parts every handler shares ----

    /// <summary>
    /// Runs one handler: a new request id and log scope, the start log, the null-context check, and the failure replies for
    /// a <see cref="HandlerFailedException"/>, a cancellation (the caller gave up) and any other exception.
    /// </summary>
    /// <param name="body">Given the request id and the call's cancellation token.</param>
    /// <param name="logStart">Logs the start; by default the handler name.</param>
    /// <param name="scope">Log scope entries after the request id.</param>
    private async Task<TReply> HandleAsync<TReply>(
        string handler,
        ServerCallContext? context,
        Func<string, TReply> failure,
        Func<long, CancellationToken, Task<TReply>> body,
        Action? logStart = null,
        KeyValuePair<string, object>[]? scope = null) {
        long reqId = Interlocked.Increment(ref _reqId);
        var scopeEntries = new Dictionary<string, object>() { [LogUtils.ReqIdContext] = reqId };
        foreach ((string key, object value) in scope ?? [])
            scopeEntries[key] = value;
        using var logContext = _logger.BeginScope(scopeEntries);

        try {
            if (logStart is null)
                _logger.LogInformation("{Handler}", handler);
            else
                logStart();

            if (context is null) {
                _logger.LogWarning("{Handler} - Null context", handler);
                return failure("No context supplied");
            }

            return await body(reqId, context.CancellationToken);
        } catch (HandlerFailedException ex) {
            return failure(ex.Message); // Logged where it was thrown
        } catch (OperationCanceledException) {
            _logger.LogWarning("{Handler} canceled", handler);
            return failure($"{handler} - Canceled");
        } catch (Exception ex) {
            _logger.LogError(ex, "{Handler} - General Fault", handler);
            return failure($"{handler} ran into a general error");
        }
    }

    /// <summary>Ends a handler with a failure reply carrying this message. The thrower logs the reason.</summary>
    private sealed class HandlerFailedException(string errMsg) : Exception(errMsg);

    /// <summary>
    /// The log and reply texts for a request that could not be posted or got a reply of the wrong type. They differ by
    /// target, and are kept as they were (typo included) since the reply's error message reaches API callers.
    /// </summary>
    private sealed record PostTexts(string PostFailedLog, string PostFailedError, string InvalidLog, string InvalidError) {
        public static readonly PostTexts ToRequestProcessor = new(
            "Failed to post request to request processor, aborting", "Failed to post request",
            "Received invalid response", "Got an invalid repsonse");
        public static readonly PostTexts ToQuotesService = new(
            "Failed to post request to quotes service, aborting", "Failed to post request to quotes service",
            "Received invalid response from quotes service", "Got an invalid repsonse from quotes service");
        public static readonly PostTexts ToSearchService = new(
            "Failed to post request to search service, aborting", "Failed to post request",
            "Received invalid response", "Got an invalid repsonse");
        public static readonly PostTexts Plain = new(
            "Failed to post request, aborting", "Failed to post request",
            "Received invalid response", "Got an invalid response");
    }

    /// <summary>Posts a request and waits for its reply; a request the service would not take fails the handler.</summary>
    private async Task<object?> PostAndAwaitAsync<TInput>(
        string handler, Func<CancellationTokenSource, TInput> makeInput, Func<TInput, bool> post, PostTexts texts, CancellationToken ct)
        where TInput : ServiceInputBase {
        (bool posted, object? response) = await ServiceRequests.PostAndAwaitAsync(makeInput, post, ct);
        if (!posted) {
            _logger.LogWarning("{Handler} - {Problem}", handler, texts.PostFailedLog);
            throw new HandlerFailedException(texts.PostFailedError);
        }
        return response;
    }

    /// <summary>As above, and a reply that is not a <typeparamref name="TResult"/> fails the handler.</summary>
    private async Task<TResult> PostAndAwaitAsync<TInput, TResult>(
        string handler, Func<CancellationTokenSource, TInput> makeInput, Func<TInput, bool> post, PostTexts texts, CancellationToken ct)
        where TInput : ServiceInputBase {
        object? response = await PostAndAwaitAsync(handler, makeInput, post, texts, ct);
        if (response is not TResult result) {
            _logger.LogWarning("{Handler} - {Problem}", handler, texts.InvalidLog);
            throw new HandlerFailedException(texts.InvalidError);
        }
        return result;
    }

    private Task<IDictionary<string, decimal>> GetPricesAsync(string handler, long reqId, List<string> symbols, CancellationToken ct) =>
        PostAndAwaitAsync<QuoteServiceFillPricesForSymbolsInput, IDictionary<string, decimal>>(handler,
            cts => new(reqId, cts, symbols), _quotesService.PostRequest, PostTexts.ToQuotesService, ct);

    private async Task WaitUntilReadyAsync(string handler, string serviceName, Task ready, CancellationToken ct) {
        _logger.LogInformation("{Handler} - Waiting for {ServiceName} to be ready", handler, serviceName);
        await ready.WaitAsync(ct);
        _logger.LogInformation("{Handler} - {ServiceName} is ready", handler, serviceName);
    }
}
