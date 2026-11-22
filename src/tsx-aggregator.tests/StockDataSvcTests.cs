using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The gRPC handlers of StockDataSvc (code review R40, part 1): GetStocksData and GetStocksDetail fill each price from
/// the quote service (the list drops a company without one, the details keep it at price 0), GetStockSearchResults
/// passes the search service's results through, SetPriorityCompanies and GetPriorityCompanies reach the registry
/// through a running (paused) RawCollector, and GetDashboardStats passes the processor's reply through. The request
/// processor, quote service and search service are mocks that complete the posted requests.
/// </summary>
public sealed class StockDataSvcTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string Tsx = Constants.TsxExchange;

    private readonly Mock<IStocksDataRequestsProcessor> _processor = new(MockBehavior.Strict);
    private readonly Mock<IQuoteService> _quotes = new(MockBehavior.Strict);
    private readonly Mock<ISearchService> _search = new(MockBehavior.Strict);
    private readonly Mock<IDbmService> _dbm = new();
    private readonly List<StocksDataRequestsInputBase> _processorRequests = [];
    private readonly List<IReadOnlyList<string>> _quoteRequests = [];
    private readonly Registry _registry = TestDataFactory.CreateRegistryWithInstruments(
        ("NMR", "NMR.UN", Tsx), ("CSH", "CSH", Tsx), ("BLQ", "BLQ", Tsx));
    private ServiceProvider? _services;
    private RawCollector? _collector;

    public StockDataSvcTests() {
        var ready = new TaskCompletionSource();
        ready.SetResult();
        _ = _quotes.Setup(q => q.QuoteServiceReady).Returns(ready);
        _ = _search.Setup(s => s.SearchServiceReady).Returns(ready);

        // The RawCollector restores a paused state with both fetches a day away, so it never scrapes.
        DateTime tomorrow = DateTime.UtcNow.AddDays(1);
        _ = _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, new ApplicationCommonState(true, tomorrow, tomorrow, null, InstrumentKey.Empty)));
        _ = _dbm.Setup(d => d.GetCommonServiceState("RawCollector", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, true));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_collector is not null)
            await _collector.StopAsync(CancellationToken.None);
        if (_services is not null)
            await _services.DisposeAsync();
    }

    private StockDataSvc BuildService() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<StockDataSvc>>(new NullLogger<StockDataSvc>());
        _ = services.AddSingleton<ILogger<RawCollector>>(new NullLogger<RawCollector>());
        _ = services.AddSingleton<ILogger<RawCollectorFsm>>(new NullLogger<RawCollectorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        _ = services.AddSingleton(_registry);
        _ = services.AddSingleton(new Mock<IHttpClientFactory>().Object);
        _ = services.AddSingleton<RawCollector>();
        _ = services.AddSingleton(_processor.Object);
        _ = services.AddSingleton(_quotes.Object);
        _ = services.AddSingleton(_search.Object);
        _services = services.BuildServiceProvider();
        return new StockDataSvc(_services);
    }

    private async Task<StockDataSvc> BuildServiceWithRunningRawCollector() {
        StockDataSvc svc = BuildService();
        _collector = _services!.GetRequiredService<RawCollector>();
        await _collector.StartAsync(CancellationToken.None);
        return svc;
    }

    private void ProcessorReplies(object? reply) =>
        _processor.Setup(p => p.PostRequest(It.IsAny<StocksDataRequestsInputBase>()))
            .Callback<StocksDataRequestsInputBase>(req => {
                _processorRequests.Add(req);
                _ = req.Completed.TrySetResult(reply);
            })
            .Returns(true);

    private void QuotesReply(object? prices) =>
        _quotes.Setup(q => q.PostRequest(It.IsAny<QuoteServiceInputBase>()))
            .Callback<QuoteServiceInputBase>(req => {
                _quoteRequests.Add([.. ((QuoteServiceFillPricesForSymbolsInput)req).Symbols]);
                _ = req.Completed.TrySetResult(prices);
            })
            .Returns(true);

    private static GetStocksDataReplyItem Item(string instrumentSymbol) => new() {
        Exchange = Tsx,
        CompanySymbol = instrumentSymbol.Split('.')[0],
        InstrumentSymbol = instrumentSymbol,
        CompanyName = instrumentSymbol + " Corp",
        CurrentNumShares = 50_000_000,
        CurrentTotalShareholdersEquity = 900_000_000,
        PerSharePrice = 0M,
    };

    private static GetStocksDataReply StocksReply(params string[] instrumentSymbols) {
        var reply = new GetStocksDataReply { Success = true };
        reply.StocksData.AddRange(instrumentSymbols.Select(Item));
        return reply;
    }

    // ---- GetStocksData ----

    [Fact]
    public async Task GetStocksData_FillsPricesAndDropsCompaniesWithoutAQuote() {
        ProcessorReplies(StocksReply("NMR.UN", "CSH", "BLQ", "XTR"));
        QuotesReply(new Dictionary<string, decimal> { ["NMR.UN"] = 18.42M, ["BLQ"] = 0.035M, ["XTR"] = 121.5M });

        GetStocksDataReply reply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        reply.StocksData.Select(i => (i.InstrumentSymbol, (decimal)i.PerSharePrice)).Should().Equal(
            ("NMR.UN", 18.42M), ("BLQ", 0.035M), ("XTR", 121.5M));
        _processorRequests.Should().ContainSingle().Which.Should().BeOfType<GetStocksForExchangeRequest>()
            .Which.Exchange.Should().Be(Tsx);
        _quoteRequests.Should().ContainSingle().Which.Should().Equal("NMR.UN", "CSH", "BLQ", "XTR");
    }

    [Fact]
    public async Task GetStocksData_ProcessorFailure_IsPassedThrough() {
        ProcessorReplies(new GetStocksDataReply { Success = false, ErrorMessage = "connection refused" });
        QuotesReply(new Dictionary<string, decimal>());

        GetStocksDataReply reply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("connection refused");
        reply.StocksData.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStocksData_ProcessorRejectsThePost_Fails() {
        _ = _processor.Setup(p => p.PostRequest(It.IsAny<StocksDataRequestsInputBase>())).Returns(false);

        GetStocksDataReply reply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("Failed to post request");
    }

    [Fact]
    public async Task GetStocksData_QuoteServiceRejectsThePost_Fails() {
        ProcessorReplies(StocksReply("NMR.UN"));
        _ = _quotes.Setup(q => q.PostRequest(It.IsAny<QuoteServiceInputBase>())).Returns(false);

        GetStocksDataReply reply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.StocksData.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStocksData_WrongReplyTypes_Fail() {
        ProcessorReplies(new GetStocksDetailReply { Success = true });
        GetStocksDataReply wrongProcessorReply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        ProcessorReplies(StocksReply("NMR.UN"));
        QuotesReply(new List<decimal> { 18.42M });
        GetStocksDataReply wrongQuotesReply = await BuildService()
            .GetStocksData(new GetStocksDataRequest { Exchange = Tsx }, new TestServerCallContext()).WaitAsync(Deadline);

        wrongProcessorReply.Success.Should().BeFalse();
        wrongQuotesReply.Success.Should().BeFalse();
        wrongQuotesReply.StocksData.Should().BeEmpty();
    }

    // ---- GetStocksDetail ----

    private static GetStocksDetailReply DetailReply(string instrumentSymbol) =>
        new() { Success = true, StockDetail = Item(instrumentSymbol) };

    [Fact]
    public async Task GetStocksDetail_FillsThePrice() {
        ProcessorReplies(DetailReply("NMR.UN"));
        QuotesReply(new Dictionary<string, decimal> { ["NMR.UN"] = 18.42M });

        GetStocksDetailReply reply = await BuildService()
            .GetStocksDetail(new GetStocksDetailRequest { Exchange = Tsx, InstrumentSymbol = "NMR.UN" }, new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        reply.StockDetail.InstrumentSymbol.Should().Be("NMR.UN");
        ((decimal)reply.StockDetail.PerSharePrice).Should().Be(18.42M);
        GetStockDetailsForExchangeRequest posted = _processorRequests.Should().ContainSingle()
            .Which.Should().BeOfType<GetStockDetailsForExchangeRequest>().Subject;
        posted.Exchange.Should().Be(Tsx);
        posted.InstrumentSymbol.Should().Be("NMR.UN");
        _quoteRequests.Should().ContainSingle().Which.Should().Equal("NMR.UN");
    }

    [Fact]
    public async Task GetStocksDetail_NoQuote_KeepsTheCompanyAtPriceZero() {
        // Unlike the list, the details page still shows the company; price 0 makes market cap and upside n/a.
        ProcessorReplies(DetailReply("CSH"));
        QuotesReply(new Dictionary<string, decimal>());

        GetStocksDetailReply reply = await BuildService()
            .GetStocksDetail(new GetStocksDetailRequest { Exchange = Tsx, InstrumentSymbol = "CSH" }, new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        reply.StockDetail.InstrumentSymbol.Should().Be("CSH");
        ((decimal)reply.StockDetail.PerSharePrice).Should().Be(0M);
        CompanyFullDetailReport.FromReplyItem(reply.StockDetail).CurMarketCap.Should().BeNull();
    }

    [Fact]
    public async Task GetStocksDetail_NoDetail_IsReturnedWithoutAskingForAQuote() {
        ProcessorReplies(new GetStocksDetailReply { Success = false, ErrorMessage = "No data found" });

        GetStocksDetailReply reply = await BuildService()
            .GetStocksDetail(new GetStocksDetailRequest { Exchange = Tsx, InstrumentSymbol = "NOPE" }, new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("No data found");
        reply.StockDetail.Should().BeNull();
        _quotes.Verify(q => q.PostRequest(It.IsAny<QuoteServiceInputBase>()), Times.Never);
    }

    // ---- GetStockSearchResults ----

    [Fact]
    public async Task GetStockSearchResults_ReturnsTheSearchServiceResultsInOrder() {
        string? postedTerm = null;
        List<GetStockSearchResultsReplyItem> results = [
            new() { Exchange = Tsx, InstrumentSymbol = "NMR.UN", CompanyName = "Northern Maple Rail Corp" },
            new() { Exchange = Tsx, InstrumentSymbol = "NMX", CompanyName = "Northern Mining Exploration Ltd" },
        ];
        _ = _search.Setup(s => s.PostRequest(It.IsAny<SearchServiceInputBase>()))
            .Callback<SearchServiceInputBase>(req => {
                postedTerm = ((SearchServiceQuickSearchRequestInput)req).SearchTerm;
                _ = req.Completed.TrySetResult(results);
            })
            .Returns(true);

        GetStockSearchResultsReply reply = await BuildService()
            .GetStockSearchResults(new GetStockSearchResultsRequest { SearchTerm = "nor" }, new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        postedTerm.Should().Be("nor");
        reply.SearchResults.Select(r => (r.Exchange, r.InstrumentSymbol, r.CompanyName)).Should().Equal(
            (Tsx, "NMR.UN", "Northern Maple Rail Corp"), (Tsx, "NMX", "Northern Mining Exploration Ltd"));
    }

    [Fact]
    public async Task GetStockSearchResults_SearchServiceRejectsThePost_Fails() {
        _ = _search.Setup(s => s.PostRequest(It.IsAny<SearchServiceInputBase>())).Returns(false);

        GetStockSearchResultsReply reply = await BuildService()
            .GetStockSearchResults(new GetStockSearchResultsRequest { SearchTerm = "nor" }, new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.SearchResults.Should().BeEmpty();
    }

    // ---- SetPriorityCompanies / GetPriorityCompanies ----

    [Fact]
    public async Task SetPriorityCompanies_QueuesTheSymbolsInTheRegistry() {
        StockDataSvc svc = await BuildServiceWithRunningRawCollector();
        var request = new SetPriorityCompaniesRequest();
        request.CompanySymbols.AddRange(["CSH", "NMR", "CSH", "UNKNOWN"]);

        StockDataServiceReply reply = await svc.SetPriorityCompanies(request, new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        _registry.GetPriorityCompanySymbols().Should().Equal("CSH", "NMR", "UNKNOWN");
    }

    [Fact]
    public async Task GetPriorityCompanies_ReturnsTheRegistryQueue() {
        _ = _registry.SetPriorityCompanies(["BLQ", "NMR"]);
        StockDataSvc svc = await BuildServiceWithRunningRawCollector();

        GetPriorityCompaniesReply reply = await svc.GetPriorityCompanies(new GetPriorityCompaniesRequest(), new TestServerCallContext())
            .WaitAsync(Deadline);

        reply.Success.Should().BeTrue();
        reply.CompanySymbols.Should().Equal("BLQ", "NMR");
    }

    // ---- GetDashboardStats ----

    [Fact]
    public async Task GetDashboardStats_ReturnsTheProcessorReply() {
        var processorReply = new GetDashboardStatsReply { Success = true, TotalActiveInstruments = 2_184, UnprocessedEventCount = 14 };
        processorReply.RawReportCounts.Add(new DashboardRawReportCount { ReportType = 1, Count = 9_120 });
        ProcessorReplies(processorReply);

        GetDashboardStatsReply reply = await BuildService()
            .GetDashboardStats(new GetDashboardStatsRequest(), new TestServerCallContext()).WaitAsync(Deadline);

        reply.Should().Be(processorReply);
        _processorRequests.Should().ContainSingle().Which.Should().BeOfType<GetDashboardStatsRequestInput>();
    }

    [Fact]
    public async Task GetDashboardStats_WrongReplyType_Fails() {
        ProcessorReplies(new GetStocksDataReply { Success = true });

        GetDashboardStatsReply reply = await BuildService()
            .GetDashboardStats(new GetDashboardStatsRequest(), new TestServerCallContext()).WaitAsync(Deadline);

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("Got an invalid response");
    }
}
