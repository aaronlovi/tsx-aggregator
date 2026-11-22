using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using stock_market_webapi.Controllers;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using static tsx_aggregator.Services.StockDataService;

namespace tsx_aggregator.tests;

/// <summary>
/// Review R88: the REST request path in <see cref="CompaniesController"/> had almost no coverage. Covers the top,
/// bottom and paged company lists (score cutoff, ordering by score then by the average of the two est. returns with
/// null averages last, paging), company detail found / not found, quick search mapping, the missing-data page and its
/// error paths, the dashboard stats mapping, the dashboard aggregates maths (checked by hand), and failure replies.
/// </summary>
public class CompaniesControllerListTests {
    private readonly Mock<StockDataServiceClient> _client = new();

    private CompaniesController Controller => new(_client.Object);

    private static AsyncUnaryCall<T> UnaryCall<T>(T reply) => new(
        Task.FromResult(reply),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => [],
        () => { });

    private void SetupStocksData(GetStocksDataReply reply) =>
        _ = _client
            .Setup(c => c.GetStocksDataAsync(
                It.IsAny<GetStocksDataRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(reply));

    private void SetupStocksData(params GetStocksDataReplyItem[] items) {
        var reply = new GetStocksDataReply { Success = true };
        reply.StocksData.AddRange(items);
        SetupStocksData(reply);
    }

    /// <summary>
    /// A company that passes all 16 checks with the defaults: price 50, 10M shares (market cap 500M), book value 200M,
    /// equity 500M, long-term debt 100M, dividends 5M. Est. return from cash flow = 100 * (netCashFlow + 5M) / cap,
    /// from owner earnings = 100 * ownerEarnings / cap; both must stay between 5% and 40% to keep checks 6-9.
    /// <paramref name="checksLost"/> (0-5) drops checks that do not depend on the returns, one at a time.
    /// </summary>
    private static GetStocksDataReplyItem Item(
        string symbol,
        int checksLost = 0,
        decimal price = 50M,
        long shares = 10_000_000,
        long netCashFlow = 60_000_000,
        long ownerEarnings = 55_000_000) {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX",
            CompanySymbol = symbol,
            InstrumentSymbol = symbol,
            CompanyName = $"{symbol} Corp",
            InstrumentName = $"{symbol} Corp Common Shares",
            PerSharePrice = price,
            CurrentNumShares = shares,
            CurrentLongTermDebt = 100_000_000,
            CurrentTotalShareholdersEquity = 500_000_000,
            CurrentBookValue = 200_000_000,
            AverageNetCashFlow = netCashFlow,
            AverageOwnerEarnings = ownerEarnings,
            CurrentDividendsPaid = 5_000_000,
            CurrentAdjustedRetainedEarnings = 50_000_000,
            OldestRetainedEarnings = 30_000_000,
            NumAnnualProcessedCashFlowReports = 5,
            RoeCashFlowNumerator = 60_000_000,
            RoeCashFlowEquity = 500_000_000,
            RoeOwnerEarningsNumerator = 55_000_000,
            RoeOwnerEarningsEquity = 500_000_000,
        };
        if (checksLost < 5) {
            for (int year = 2025; year > 2020; year--)
                item.RoeOwnerEarningsByYear.Add(new AnnualRoe { YearEnd = $"{year}-12-31", Roe = 0.2M });
        }
        if (checksLost >= 1) item.NumAnnualProcessedCashFlowReports = 3;          // check 12 fails
        if (checksLost >= 2) item.OldestRetainedEarnings = 80_000_000;            // check 13 fails
        if (checksLost >= 3) item.RoeCashFlowNumerator = 10_000_000;              // check 14 fails
        if (checksLost >= 4) item.RoeOwnerEarningsNumerator = 10_000_000;         // check 15 fails
        return item;
    }

    /// <summary>No quote: market cap and both est. returns are null; scores 11 (checks 3 and 6-9 n/a).</summary>
    private static GetStocksDataReplyItem NoPriceItem(string symbol) => Item(symbol, price: 0M);

    /// <summary>Priced (cap 10M) but fails or lacks every check: score 0, est. returns null.</summary>
    private static GetStocksDataReplyItem DistressedItem(string symbol) {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX",
            CompanySymbol = symbol,
            InstrumentSymbol = symbol,
            CompanyName = $"{symbol} Corp",
            InstrumentName = $"{symbol} Corp Common Shares",
            PerSharePrice = 10M,
            CurrentNumShares = 1_000_000,
            CurrentLongTermDebt = 100_000_000,
            CurrentTotalShareholdersEquity = -1,
            CurrentBookValue = -1,
            CurrentDividendsPaid = 0,
            CurrentAdjustedRetainedEarnings = -1,
            OldestRetainedEarnings = 5,
            NumAnnualProcessedCashFlowReports = 0,
            RoeCashFlowNumerator = 1,
            RoeCashFlowEquity = -1,
            RoeOwnerEarningsNumerator = 1,
            RoeOwnerEarningsEquity = -1,
        };
        return item;
    }

    private static T OkValue<T>(IConvertToActionResult result) {
        ObjectResult ok = result.Convert().Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeAssignableTo<T>().Subject;
    }

    private static object? ErrorOf(object? value) => value?.GetType().GetProperty("error")?.GetValue(value);

    // ---- GetCompanies ----

    [Fact]
    public void Item_Helper_ScoresAsDocumented() {
        // Guards the fixtures the other tests rely on.
        _ = Enumerable.Range(0, 6).Select(n => CompanyFullDetailReport.FromReplyItem(Item("X", n)).OverallScore)
            .Should().Equal(16, 15, 14, 13, 12, 11);
        _ = CompanyFullDetailReport.FromReplyItem(NoPriceItem("X")).OverallScore.Should().Be(11);
        _ = CompanyFullDetailReport.FromReplyItem(DistressedItem("X")).OverallScore.Should().Be(0);
    }

    [Fact]
    public async Task GetCompanies_KeepsOnlyScoresAtOrAboveTheCutoff() {
        SetupStocksData(Item("S11", 5), Item("S14", 2), Item("S13", 3), Item("S16"), Item("S15", 1), Item("S12", 4));

        var reports = OkValue<IEnumerable<CompanySummaryReport>>(await Controller.GetCompanies());

        _ = ScoringOptions.TopScoreCutoff.Should().Be(14);
        _ = reports.Select(r => r.InstrumentSymbol).Should().Equal("S16", "S15", "S14");
    }

    [Fact]
    public async Task GetCompanies_OrdersByScoreThenByTheAverageOfTheTwoReturns() {
        // Score 16: CF 30% / OE 6% (avg 18), CF 20% / OE 20% (avg 20), CF 10% / OE 34% (avg 22). Sorting by CF alone
        // or OE alone gives a different order from the average.
        // Score 15: avg 8 and avg 20; a 15 with a higher return still comes after every 16.
        SetupStocksData(
            Item("LOW15", 1, netCashFlow: 35_000_000, ownerEarnings: 40_000_000),
            Item("CF30", netCashFlow: 145_000_000, ownerEarnings: 30_000_000),
            Item("HIGH15", 1, netCashFlow: 95_000_000, ownerEarnings: 100_000_000),
            Item("OE34", netCashFlow: 45_000_000, ownerEarnings: 170_000_000),
            Item("EVEN20", netCashFlow: 95_000_000, ownerEarnings: 100_000_000));

        var reports = OkValue<IEnumerable<CompanySummaryReport>>(await Controller.GetCompanies()).ToList();

        _ = reports.Select(r => r.InstrumentSymbol).Should().Equal("OE34", "EVEN20", "CF30", "HIGH15", "LOW15");
        _ = reports.Select(r => r.OverallScore).Should().Equal(16, 16, 16, 15, 15);
    }

    [Fact]
    public async Task GetCompanies_MapsToCompanySummaryReport() {
        var annual = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var quarterly = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var updated = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        GetStocksDataReplyItem item = Item("LNF");
        item.CompanyName = "Leon's Furniture";
        item.MostRecentAnnualReportDate = Timestamp.FromDateTimeOffset(annual);
        item.MostRecentQuarterlyReportDate = Timestamp.FromDateTimeOffset(quarterly);
        item.LastUpdatedDate = Timestamp.FromDateTimeOffset(updated);
        SetupStocksData(item);

        CompanySummaryReport report = OkValue<IEnumerable<CompanySummaryReport>>(await Controller.GetCompanies()).Single();

        // Cap 50 * 10M = 500M. CF return 100 * (60M + 5M) / 500M = 13%; OE 100 * 55M / 500M = 11%.
        // Max price min(3 * 200M, 20 * 65M, 20 * 55M) / 10M = 60; upside (60 - 50) / 50 = 20%.
        // ROE CF 60M / 500M = 0.12; ROE OE 55M / 500M = 0.11.
        _ = report.Should().Be(new CompanySummaryReport(
            Exchange: "TSX",
            InstrumentSymbol: "LNF",
            CompanyName: "Leon's Furniture",
            PricePerShare: 50M,
            CurMarketCap: 500_000_000M,
            EstimatedNextYearTotalReturnPercentage_FromCashFlow: 13M,
            EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings: 11M,
            OverallScore: 16,
            NotAvailableCount: 0,
            MaxPrice: 60M,
            PercentageUpside: 20M,
            ReturnOnEquity_FromCashFlow: 0.12M,
            ReturnOnEquity_FromOwnerEarnings: 0.11M,
            MostRecentAnnualReportDate: annual,
            MostRecentQuarterlyReportDate: quarterly,
            LastUpdatedDate: updated));
    }

    // ---- GetBottomCompanies ----

    [Fact]
    public async Task GetBottomCompanies_OrdersByScoreThenReturnAscending_NullReturnLast() {
        SetupStocksData(
            Item("TOP16"),
            NoPriceItem("NOPRICE11"),
            Item("HIGH11", 5, netCashFlow: 95_000_000, ownerEarnings: 100_000_000),
            Item("LOW11", 5, netCashFlow: 35_000_000, ownerEarnings: 40_000_000),
            Item("MID14", 2));

        var reports = OkValue<IEnumerable<CompanySummaryReport>>(await Controller.GetBottomCompanies()).ToList();

        _ = reports.Select(r => r.InstrumentSymbol).Should().Equal("LOW11", "HIGH11", "NOPRICE11", "MID14", "TOP16");
    }

    [Fact]
    public async Task GetBottomCompanies_ReturnsAtMostThirty_TheLowestScores() {
        var items = new List<GetStocksDataReplyItem> { Item("BEST1"), Item("BEST2") };
        for (int i = 0; i < 30; i++)
            items.Add(Item($"LOW{i:00}", 1));
        SetupStocksData([.. items]);

        var reports = OkValue<IEnumerable<CompanySummaryReport>>(await Controller.GetBottomCompanies()).ToList();

        _ = reports.Should().HaveCount(30);
        _ = reports.Should().OnlyContain(r => r.OverallScore == 15);
    }

    // ---- GetAllCompanies ----

    private void SetupFiveCompanies() =>
        SetupStocksData(Item("S13", 3), Item("S16"), Item("S12", 4), Item("S14", 2), Item("S15", 1));

    [Fact]
    public async Task GetAllCompanies_FirstPage() {
        SetupFiveCompanies();

        var page = OkValue<PagedCompanySummaryReports>(await Controller.GetAllCompanies(1, 2));

        _ = page.PagingData.Should().Be(new PagingData(5, 1, 2));
        _ = page.PagingData.NumPages.Should().Be(3);
        _ = page.Companies.Select(c => c.InstrumentSymbol).Should().Equal("S16", "S15");
    }

    [Fact]
    public async Task GetAllCompanies_LastPageIsPartial() {
        SetupFiveCompanies();

        var page = OkValue<PagedCompanySummaryReports>(await Controller.GetAllCompanies(3, 2));

        _ = page.PagingData.Should().Be(new PagingData(5, 3, 2));
        _ = page.Companies.Select(c => c.InstrumentSymbol).Should().Equal("S12");
    }

    [Fact]
    public async Task GetAllCompanies_PageBeyondTheEndIsEmpty_WithTheRealTotal() {
        SetupFiveCompanies();

        var page = OkValue<PagedCompanySummaryReports>(await Controller.GetAllCompanies(4, 2));

        _ = page.PagingData.Should().Be(new PagingData(5, 4, 2));
        _ = page.Companies.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllCompanies_NullAverageReturnSortsLastInDescendingOrderToo() {
        SetupStocksData(NoPriceItem("NOPRICE11"), Item("LOW11", 5, netCashFlow: 35_000_000, ownerEarnings: 40_000_000),
            Item("HIGH11", 5, netCashFlow: 95_000_000, ownerEarnings: 100_000_000));

        var page = OkValue<PagedCompanySummaryReports>(await Controller.GetAllCompanies(1, 10));

        _ = page.Companies.Select(c => c.InstrumentSymbol).Should().Equal("HIGH11", "LOW11", "NOPRICE11");
    }

    // ---- GetCompany ----

    [Fact]
    public async Task GetCompany_Found_ReturnsTheFullDetailReport() {
        GetStocksDataReplyItem item = Item("LNF");
        _ = _client
            .Setup(c => c.GetStocksDetailAsync(
                It.IsAny<GetStocksDetailRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(new GetStocksDetailReply { Success = true, StockDetail = item }));

        var report = OkValue<CompanyFullDetailReport>(await Controller.GetCompany("tsx", "LNF"));

        _ = report.InstrumentSymbol.Should().Be("LNF");
        _ = report.CompanyName.Should().Be("LNF Corp");
        _ = report.CurBookValue.Should().Be(200_000_000M);
        _ = report.CurMarketCap.Should().Be(500_000_000M);
        _ = report.OverallScore.Should().Be(16);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task GetCompany_NotFound_WhenTheReplyFailsOrHasNoDetail(bool success, bool withDetail) {
        var reply = new GetStocksDetailReply { Success = success };
        if (withDetail)
            reply.StockDetail = Item("LNF");
        _ = _client
            .Setup(c => c.GetStocksDetailAsync(
                It.IsAny<GetStocksDetailRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(reply));

        ActionResult<CompanyFullDetailReport> result = await Controller.GetCompany("TSX", "LNF");

        NotFoundObjectResult notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = ErrorOf(notFound.Value).Should().Be("No data available for LNF on TSX");
    }

    // ---- SearchCompanies ----

    [Fact]
    public async Task SearchCompanies_SendsTheTermAndMapsAtMostFiveResults() {
        GetStockSearchResultsRequest? sent = null;
        var reply = new GetStockSearchResultsReply { Success = true };
        for (int i = 1; i <= 7; i++)
            reply.SearchResults.Add(new GetStockSearchResultsReplyItem { Exchange = "TSX", InstrumentSymbol = $"L{i}", CompanyName = $"Company {i}" });
        _ = _client
            .Setup(c => c.GetStockSearchResultsAsync(
                It.IsAny<GetStockSearchResultsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<GetStockSearchResultsRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) => sent = req)
            .Returns(UnaryCall(reply));

        var results = OkValue<IEnumerable<CompanySearchResult>>(await Controller.SearchCompanies("le"));

        _ = sent!.SearchTerm.Should().Be("le");
        _ = results.Should().Equal(
            new CompanySearchResult("TSX", "Company 1", "L1"),
            new CompanySearchResult("TSX", "Company 2", "L2"),
            new CompanySearchResult("TSX", "Company 3", "L3"),
            new CompanySearchResult("TSX", "Company 4", "L4"),
            new CompanySearchResult("TSX", "Company 5", "L5"));
    }

    // ---- GetMissingDataCompanies ----

    [Fact]
    public async Task GetMissingDataCompanies_SendsThePageAndMapsTheReply() {
        GetInstrumentsWithNoRawReportsRequest? sent = null;
        var reply = new GetInstrumentsWithNoRawReportsReply { Success = true, TotalItems = 41, PageNumber = 3, PageSize = 20 };
        reply.Instruments.Add(new InstrumentInfoItem {
            InstrumentId = 1234, Exchange = "TSX", CompanySymbol = "BN", InstrumentSymbol = "BN.PR.B",
            CompanyName = "Brookfield Corp", InstrumentName = "Brookfield Corp Pref Series 2",
        });
        _ = _client
            .Setup(c => c.GetInstrumentsWithNoRawReportsAsync(
                It.IsAny<GetInstrumentsWithNoRawReportsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<GetInstrumentsWithNoRawReportsRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) => sent = req)
            .Returns(UnaryCall(reply));

        var page = OkValue<PagedInstrumentInfoReports>(await Controller.GetMissingDataCompanies("TSX", 3, 20));

        _ = sent.Should().Be(new GetInstrumentsWithNoRawReportsRequest { Exchange = "TSX", PageNumber = 3, PageSize = 20 });
        _ = page.PagingData.Should().Be(new PagingData(41, 3, 20));
        _ = page.PagingData.NumPages.Should().Be(3);
        _ = page.Instruments.Should().Equal(
            new InstrumentInfo(1234, "TSX", "BN", "BN.PR.B", "Brookfield Corp", "Brookfield Corp Pref Series 2"));
    }

    [Fact]
    public async Task GetMissingDataCompanies_FailedReply_IsBadRequestWithTheError() {
        _ = _client
            .Setup(c => c.GetInstrumentsWithNoRawReportsAsync(
                It.IsAny<GetInstrumentsWithNoRawReportsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(new GetInstrumentsWithNoRawReportsReply { Success = false, ErrorMessage = "bad page" }));

        ActionResult<PagedInstrumentInfoReports> result = await Controller.GetMissingDataCompanies("TSX", 1, 20);

        BadRequestObjectResult bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = ErrorOf(bad.Value).Should().Be("bad page");
    }

    [Fact]
    public async Task GetMissingDataCompanies_RpcFailure_Is500WithTheMessage() {
        _ = _client
            .Setup(c => c.GetInstrumentsWithNoRawReportsAsync(
                It.IsAny<GetInstrumentsWithNoRawReportsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "aggregator down")));

        ActionResult<PagedInstrumentInfoReports> result = await Controller.GetMissingDataCompanies("TSX", 1, 20);

        ObjectResult error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        _ = error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        _ = ErrorOf(error.Value).Should().BeOfType<string>().Which.Should().Contain("aggregator down");
    }

    // ---- GetDashboardStats ----

    [Fact]
    public async Task GetDashboardStats_MapsEveryField() {
        var rawIngestion = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var aggregation = new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
        var nextDirectory = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
        var nextInstrument = new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);
        var reply = new GetDashboardStatsReply {
            Success = true,
            TotalActiveInstruments = 1800,
            TotalObsoletedInstruments = 250,
            InstrumentsWithProcessedReports = 1500,
            InstrumentsWithoutProcessedReports = 300,
            MostRecentRawIngestion = Timestamp.FromDateTimeOffset(rawIngestion),
            MostRecentAggregation = Timestamp.FromDateTimeOffset(aggregation),
            UnprocessedEventCount = 7,
            NextFetchDirectoryTime = Timestamp.FromDateTimeOffset(nextDirectory),
            NextFetchInstrumentDataTime = Timestamp.FromDateTimeOffset(nextInstrument),
            // NextFetchQuotesTime and NextAggregatorCycleTime unset: those services are off.
        };
        reply.RawReportCounts.Add(new DashboardRawReportCount { ReportType = 1, Count = 9000 });
        reply.RawReportCounts.Add(new DashboardRawReportCount { ReportType = 3, Count = 8000 });
        reply.RawReportCounts.Add(new DashboardRawReportCount { ReportType = 9, Count = 2 });
        _ = _client
            .Setup(c => c.GetDashboardStatsAsync(
                It.IsAny<GetDashboardStatsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(reply));

        var stats = OkValue<DashboardStatsResponse>(await Controller.GetDashboardStats());

        _ = stats.Should().BeEquivalentTo(new DashboardStatsResponse(
            TotalActiveInstruments: 1800,
            TotalObsoletedInstruments: 250,
            InstrumentsWithProcessedReports: 1500,
            InstrumentsWithoutProcessedReports: 300,
            MostRecentRawIngestion: rawIngestion,
            MostRecentAggregation: aggregation,
            UnprocessedEventCount: 7,
            RawReportCounts: [
                new RawReportCountItem(1, "Cash Flow", 9000),
                new RawReportCountItem(3, "Balance Sheet", 8000),
                new RawReportCountItem(9, "Unknown (9)", 2),
            ],
            NextFetchDirectoryTime: nextDirectory,
            NextFetchInstrumentDataTime: nextInstrument,
            NextFetchQuotesTime: null,
            NextAggregatorCycleTime: null), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task GetDashboardStats_FailedReply_IsBadRequest_AndRpcFailureIs500() {
        _ = _client
            .Setup(c => c.GetDashboardStatsAsync(
                It.IsAny<GetDashboardStatsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(new GetDashboardStatsReply { Success = false, ErrorMessage = "db down" }));

        ActionResult<DashboardStatsResponse> failed = await Controller.GetDashboardStats();

        _ = ErrorOf(failed.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value).Should().Be("db down");

        _ = _client
            .Setup(c => c.GetDashboardStatsAsync(
                It.IsAny<GetDashboardStatsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "too slow")));

        ActionResult<DashboardStatsResponse> threw = await Controller.GetDashboardStats();

        _ = threw.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    // ---- GetDashboardAggregates ----

    [Fact]
    public async Task GetDashboardAggregates_ComputesTheNumbers() {
        // A: score 16, cap 500M, CF (60+5)/500 = 13%, OE 55/500 = 11%.
        // B: score 16, cap 50 * 8M = 400M, CF (95+5)/400 = 25%, OE 75/400 = 18.75%.
        // C: score 15, cap 500M, CF (35+5)/500 = 8%, OE 40/500 = 8%.
        // D: score 11, no price: counted, but not in the price-based figures.
        // E: score 0, cap 10 * 1M = 10M, both returns null.
        SetupStocksData(
            Item("A"),
            Item("B", shares: 8_000_000, netCashFlow: 95_000_000, ownerEarnings: 75_000_000),
            Item("C", 1, netCashFlow: 35_000_000, ownerEarnings: 40_000_000),
            NoPriceItem("D"),
            DistressedItem("E"));

        var agg = OkValue<DashboardAggregatesResponse>(await Controller.GetDashboardAggregates());

        _ = agg.TotalCompanies.Should().Be(5);
        _ = agg.CompaniesWithPriceData.Should().Be(4);
        _ = agg.CompaniesWithoutPriceData.Should().Be(1);
        _ = agg.CompaniesPassingAllChecks.Should().Be(2);
        // Averages and medians skip E's null returns: (13 + 25 + 8) / 3 = 15.333; (11 + 18.75 + 8) / 3 = 12.583.
        _ = agg.AverageEstimatedReturn_FromCashFlow.Should().Be(15.33M);
        _ = agg.AverageEstimatedReturn_FromOwnerEarnings.Should().Be(12.58M);
        _ = agg.MedianEstimatedReturn_FromCashFlow.Should().Be(13M);
        _ = agg.MedianEstimatedReturn_FromOwnerEarnings.Should().Be(11M);
        // 500 + 400 + 500 + 10 (D has no cap).
        _ = agg.TotalMarketCap.Should().Be(1_410_000_000M);
        _ = agg.ScoreDistribution.Should().Equal(
            new ScoreDistributionItem(16, 2),
            new ScoreDistributionItem(15, 1),
            new ScoreDistributionItem(11, 1),
            new ScoreDistributionItem(0, 1));
        // Priced companies with score >= 1 only (E's 0 and unpriced D are left out).
        // Score 16: caps 500M and 400M -> sum 900M, mean and median 450M; CF 13 and 25 -> 19; OE 11 and 18.75 ->
        // 14.875, rounded half to even -> 14.88.
        _ = agg.ScoreCategoryStatistics.Should().Equal(
            new ScoreCategoryStats(16, 2, 900_000_000M, 450_000_000M, 450_000_000M, 19M, 19M, 14.88M, 14.88M),
            new ScoreCategoryStats(15, 1, 500_000_000M, 500_000_000M, 500_000_000M, 8M, 8M, 8M, 8M));
    }

    [Fact]
    public async Task GetDashboardAggregates_NoPricedCompanies_PriceFiguresAreZero() {
        SetupStocksData(NoPriceItem("D1"), NoPriceItem("D2"));

        var agg = OkValue<DashboardAggregatesResponse>(await Controller.GetDashboardAggregates());

        _ = agg.TotalCompanies.Should().Be(2);
        _ = agg.CompaniesWithPriceData.Should().Be(0);
        _ = agg.CompaniesWithoutPriceData.Should().Be(2);
        _ = agg.AverageEstimatedReturn_FromCashFlow.Should().Be(0M);
        _ = agg.MedianEstimatedReturn_FromOwnerEarnings.Should().Be(0M);
        _ = agg.TotalMarketCap.Should().Be(0M);
        _ = agg.ScoreDistribution.Should().Equal(new ScoreDistributionItem(11, 2));
        _ = agg.ScoreCategoryStatistics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboardAggregates_FailedReply_IsBadRequestWithTheError() {
        SetupStocksData(new GetStocksDataReply { Success = false, ErrorMessage = "not ready" });

        ActionResult<DashboardAggregatesResponse> result = await Controller.GetDashboardAggregates();

        _ = ErrorOf(result.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value).Should().Be("not ready");
    }

    // ---- Failure replies for the list endpoints ----

    public static TheoryData<string> ListActions => ["companies", "bottom", "all", "search"];

    [Theory]
    [MemberData(nameof(ListActions))]
    public async Task ListEndpoints_FailedReply_IsBadRequest(string action) {
        SetupStocksData(new GetStocksDataReply { Success = false, ErrorMessage = "boom" });
        _ = _client
            .Setup(c => c.GetStockSearchResultsAsync(
                It.IsAny<GetStockSearchResultsRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(new GetStockSearchResultsReply { Success = false, ErrorMessage = "boom" }));

        IConvertToActionResult result = action switch {
            "companies" => await Controller.GetCompanies(),
            "bottom" => await Controller.GetBottomCompanies(),
            "all" => await Controller.GetAllCompanies(1, 10),
            _ => await Controller.SearchCompanies("le"),
        };

        _ = result.Convert().Should().BeOfType<BadRequestObjectResult>();
    }

    // Review R78: the three stock-data lists answered a failed reply with the body "abc"; they now carry the
    // aggregator's error the way the dashboard aggregates endpoint always did.
    public static TheoryData<string> StockDataListActions => ["companies", "bottom", "all"];

    [Theory]
    [MemberData(nameof(StockDataListActions))]
    public async Task StockDataListEndpoints_FailedReply_CarryTheError(string action) {
        SetupStocksData(new GetStocksDataReply { Success = false, ErrorMessage = "boom" });

        IConvertToActionResult result = action switch {
            "companies" => await Controller.GetCompanies(),
            "bottom" => await Controller.GetBottomCompanies(),
            _ => await Controller.GetAllCompanies(1, 10),
        };

        _ = ErrorOf(result.Convert().Should().BeOfType<BadRequestObjectResult>().Subject.Value).Should().Be("boom");
    }
}
