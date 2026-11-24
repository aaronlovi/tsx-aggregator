using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The gRPC request path inside tsx-aggregator (code review R40, part 1): StocksDataRequestsProcessor turns the stored
/// processed reports into GetStocksDataReplyItem (every field, unset optional fields, per-year ROE), skips a row whose
/// report does not parse without failing the request, answers the details request (found, not found, DB failure) and
/// the dashboard stats request (counts, raw report counts, the RawCollector and QuoteService next-fetch times).
/// Driven through the running BackgroundService with a mocked IDbmService, as the web API's requests arrive.
/// </summary>
public sealed class StocksDataRequestsProcessorRequestTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string Tsx = Constants.TsxExchange;

    // Deliberately not the defaults, so a mapping that ignored the configuration would show.
    private static readonly ScoringOptions Scoring = new() {
        RoeConsistencyMinYears = 3, RoeConsistencyWindowYears = 6, RoeConsistencyMinUsableYears = 4
    };

    private static readonly DateTimeOffset ReportCreated = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AnnualReportDate = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset QuarterlyReportDate = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastUpdated = new(2026, 11, 19, 23, 4, 11, TimeSpan.Zero);

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private StocksDataRequestsProcessor? _processor;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_processor is not null) {
            await _processor.StopAsync(CancellationToken.None);
            _processor.Dispose();
        }
    }

    private async Task<StocksDataRequestsProcessor> StartProcessor() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<StocksDataRequestsProcessor>>(new NullLogger<StocksDataRequestsProcessor>());
        _ = services.AddSingleton(_dbm.Object);
        _ = services.AddSingleton(Options.Create(Scoring));
        _processor = new StocksDataRequestsProcessor(services.BuildServiceProvider());
        await _processor.StartAsync(CancellationToken.None);
        return _processor;
    }

    /// <summary>
    /// A processed report as the Aggregator stores it (JsonSerializer.Serialize of a CompanyReport): a mid-size
    /// company with every figure present, dividends positive as reported, and five years of ROE (OE), most recent first.
    /// </summary>
    private static JsonObject FullReportJson() => new() {
        ["CurNumShares"] = 182_400_000,
        ["Symbol"] = "NMR",
        ["Name"] = "Northern Maple Rail Corp",
        ["Exchange"] = Tsx,
        ["CurTotalShareholdersEquity"] = 2_310_500_000.40m,
        ["CurGoodwill"] = 412_000_000m,
        ["CurIntangibles"] = 96_300_000m,
        ["CurLongTermDebt"] = 845_000_000m,
        ["CurShortTermDebt"] = 120_000_000m,
        ["CurDividendsPaid"] = 61_200_000m,
        ["AverageDividendsPaid"] = 40_400_000m,
        ["CurRetainedEarnings"] = 1_085_000_000m,
        ["CurRetainedEarningsEstimated"] = true,
        ["CurAdjustedRetainedEarnings"] = 1_120_000_000m,
        ["OldestRetainedEarnings"] = -40_000_000m,
        ["OldestRetainedEarningsEstimated"] = false,
        ["CurBookValue"] = 1_802_200_000.40m,
        ["AverageNetCashFlow"] = -12_500_000m,
        ["AverageOwnerEarnings"] = 198_000_000m,
        ["LatestOwnerEarnings"] = -7_500_000m,
        ["NumAnnualProcessedCashFlowReports"] = 6,
        ["RoeCashFlowNumerator"] = 293_500_000m,
        ["RoeCashFlowEquity"] = 10_950_000_000m,
        ["RoeOwnerEarningsNumerator"] = 1_188_000_000m,
        ["RoeOwnerEarningsEquity"] = 10_950_000_000m,
        ["RoeOwnerEarningsByYear"] = new JsonArray(
            new JsonObject { ["YearEnd"] = "2025-12-31", ["Roe"] = 0.142m },
            new JsonObject { ["YearEnd"] = "2024-12-31", ["Roe"] = 0.118m },
            new JsonObject { ["YearEnd"] = "2023-12-31", ["Roe"] = null },
            new JsonObject { ["YearEnd"] = "2022-12-31", ["Roe"] = -0.031m },
            new JsonObject { ["YearEnd"] = "2021-12-31", ["Roe"] = 0.105m }),
    };

    /// <summary>The optional figures (W1) and the ROE inputs (B11) of a report.</summary>
    private static readonly string[] OptionalFields = [
        "CurTotalShareholdersEquity", "CurDividendsPaid", "AverageDividendsPaid", "CurAdjustedRetainedEarnings", "OldestRetainedEarnings",
        "CurBookValue", "AverageNetCashFlow", "AverageOwnerEarnings", "LatestOwnerEarnings",
        "RoeCashFlowNumerator", "RoeCashFlowEquity", "RoeOwnerEarningsNumerator", "RoeOwnerEarningsEquity",
    ];

    private static JsonObject ReportJsonWithOptionalFields(JsonNode? value) {
        JsonObject json = FullReportJson();
        foreach (string field in OptionalFields)
            json[field] = value?.DeepClone();
        return json;
    }

    private static ProcessedFullInstrumentReportDto Dto(string symbol, string serializedReport) => new(
        InstrumentId: 4021,
        CompanySymbol: symbol,
        CompanyName: symbol + " Holdings Inc.",
        InstrumentSymbol: symbol + ".UN",
        InstrumentName: symbol + " Holdings Trust Units",
        SerializedReport: serializedReport,
        InstrumentCreatedDate: new DateTimeOffset(2023, 4, 2, 0, 0, 0, TimeSpan.Zero),
        InstrumentObsoletedDate: null,
        ReportCreatedDate: ReportCreated,
        ReportObsoletedDate: null,
        NumAnnualCashFlowReports: 6,
        MostRecentAnnualReportDate: AnnualReportDate,
        MostRecentQuarterlyReportDate: QuarterlyReportDate,
        LastUpdatedDate: LastUpdated);

    private void SetupStocks(params ProcessedFullInstrumentReportDto[] dtos) =>
        _dbm.Setup(d => d.GetProcessedStockDataByExchange(Tsx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>.SetSuccess(dtos));

    private async Task<GetStocksDataReply> RequestStocks() {
        StocksDataRequestsProcessor processor = await StartProcessor();
        using var req = new GetStocksForExchangeRequest(1, Tsx, null);
        processor.PostRequest(req).Should().BeTrue();
        object? reply = await req.Completed.Task.WaitAsync(Deadline);
        return reply.Should().BeOfType<GetStocksDataReply>().Subject;
    }

    private async Task<GetStocksDataReplyItem> RequestSingleItem(JsonObject json) {
        SetupStocks(Dto("NMR", json.ToJsonString()));
        GetStocksDataReply reply = await RequestStocks();
        reply.Success.Should().BeTrue();
        return reply.StocksData.Should().ContainSingle().Subject;
    }

    private async Task<GetStocksDetailReply> RequestDetail(string instrumentSymbol) {
        StocksDataRequestsProcessor processor = await StartProcessor();
        using var req = new GetStockDetailsForExchangeRequest(1, Tsx, instrumentSymbol, null);
        processor.PostRequest(req).Should().BeTrue();
        object? reply = await req.Completed.Task.WaitAsync(Deadline);
        return reply.Should().BeOfType<GetStocksDetailReply>().Subject;
    }

    private async Task<GetDashboardStatsReply> RequestDashboardStats() {
        StocksDataRequestsProcessor processor = await StartProcessor();
        using var req = new GetDashboardStatsRequestInput(1, null);
        processor.PostRequest(req).Should().BeTrue();
        object? reply = await req.Completed.Task.WaitAsync(Deadline);
        return reply.Should().BeOfType<GetDashboardStatsReply>().Subject;
    }

    // ---- GetStocksData: the reply item mapping ----

    [Fact]
    public async Task StocksRequest_FullReport_MapsEveryField() {
        GetStocksDataReplyItem item = await RequestSingleItem(FullReportJson());

        item.Exchange.Should().Be(Tsx);
        item.CompanySymbol.Should().Be("NMR");
        item.InstrumentSymbol.Should().Be("NMR.UN");
        item.CompanyName.Should().Be("NMR Holdings Inc.");
        item.InstrumentName.Should().Be("NMR Holdings Trust Units");
        item.CreatedDate.ToDateTimeOffset().Should().Be(ReportCreated);
        item.MostRecentAnnualReportDate.ToDateTimeOffset().Should().Be(AnnualReportDate);
        item.MostRecentQuarterlyReportDate.ToDateTimeOffset().Should().Be(QuarterlyReportDate);
        item.LastUpdatedDate.ToDateTimeOffset().Should().Be(LastUpdated);
        item.NumAnnualProcessedCashFlowReports.Should().Be(6);

        item.CurrentNumShares.Should().Be(182_400_000);
        item.CurrentGoodwill.Should().Be(412_000_000);
        item.CurrentIntangibles.Should().Be(96_300_000);
        item.CurrentLongTermDebt.Should().Be(845_000_000);
        item.CurrentShortTermDebt.Should().Be(120_000_000);
        item.CurrentTotalShareholdersEquity.Should().Be(2_310_500_000);
        item.CurrentDividendsPaid.Should().Be(61_200_000);
        item.AverageDividendsPaid.Should().Be(40_400_000);
        item.CurrentAdjustedRetainedEarnings.Should().Be(1_120_000_000);
        item.OldestRetainedEarnings.Should().Be(-40_000_000);
        item.CurrentBookValue.Should().Be(1_802_200_000);
        item.AverageNetCashFlow.Should().Be(-12_500_000);
        item.AverageOwnerEarnings.Should().Be(198_000_000);
        item.LatestOwnerEarnings.Should().Be(-7_500_000);
        item.CurrentRetainedEarningsEstimated.Should().BeTrue();
        item.OldestRetainedEarningsEstimated.Should().BeFalse();

        item.RoeCashFlowNumerator.Should().Be(293_500_000);
        item.RoeCashFlowEquity.Should().Be(10_950_000_000);
        item.RoeOwnerEarningsNumerator.Should().Be(1_188_000_000);
        item.RoeOwnerEarningsEquity.Should().Be(10_950_000_000);
        item.RoeOwnerEarningsByYear.Select(y => y.YearEnd).Should().Equal(
            "2025-12-31", "2024-12-31", "2023-12-31", "2022-12-31", "2021-12-31");
        item.RoeOwnerEarningsByYear.Select(y => y.Roe is null ? (decimal?)null : (decimal)y.Roe).Should().Equal(
            0.142m, 0.118m, null, -0.031m, 0.105m);

        item.RoeConsistencyMinYears.Should().Be(3);
        item.RoeConsistencyWindowYears.Should().Be(6);
        item.RoeConsistencyMinUsableYears.Should().Be(4);

        // Filled in later by StockDataSvc from the quote service.
        ((decimal)item.PerSharePrice).Should().Be(0m);
    }

    [Fact]
    public async Task StocksRequest_MissingOptionalFigures_LeavesThemUnset() {
        // Figures the source data did not have are JSON null in the report (W1).
        GetStocksDataReplyItem item = await RequestSingleItem(ReportJsonWithOptionalFields(null));

        item.HasCurrentTotalShareholdersEquity.Should().BeFalse();
        item.HasCurrentDividendsPaid.Should().BeFalse();
        item.HasAverageDividendsPaid.Should().BeFalse();
        item.HasCurrentAdjustedRetainedEarnings.Should().BeFalse();
        item.HasOldestRetainedEarnings.Should().BeFalse();
        item.HasCurrentBookValue.Should().BeFalse();
        item.HasAverageNetCashFlow.Should().BeFalse();
        item.HasAverageOwnerEarnings.Should().BeFalse();
        item.HasLatestOwnerEarnings.Should().BeFalse();
        item.HasRoeCashFlowNumerator.Should().BeFalse();
        item.HasRoeCashFlowEquity.Should().BeFalse();
        item.HasRoeOwnerEarningsNumerator.Should().BeFalse();
        item.HasRoeOwnerEarningsEquity.Should().BeFalse();
    }

    [Fact]
    public async Task StocksRequest_ZeroOptionalFigures_AreSetToZeroNotLeftUnset() {
        // 0 is a figure (it fails the sign-rule checks); unset is no figure (n/a). They must stay distinct on the wire.
        GetStocksDataReplyItem item = await RequestSingleItem(ReportJsonWithOptionalFields(JsonValue.Create(0m)));

        item.HasCurrentTotalShareholdersEquity.Should().BeTrue();
        item.HasCurrentDividendsPaid.Should().BeTrue();
        item.HasAverageDividendsPaid.Should().BeTrue();
        item.HasCurrentAdjustedRetainedEarnings.Should().BeTrue();
        item.HasOldestRetainedEarnings.Should().BeTrue();
        item.HasCurrentBookValue.Should().BeTrue();
        item.HasAverageNetCashFlow.Should().BeTrue();
        item.HasAverageOwnerEarnings.Should().BeTrue();
        item.HasLatestOwnerEarnings.Should().BeTrue();
        item.HasRoeCashFlowNumerator.Should().BeTrue();
        item.HasRoeCashFlowEquity.Should().BeTrue();
        item.HasRoeOwnerEarningsNumerator.Should().BeTrue();
        item.HasRoeOwnerEarningsEquity.Should().BeTrue();
        item.CurrentTotalShareholdersEquity.Should().Be(0);
        item.RoeOwnerEarningsEquity.Should().Be(0);
    }

    [Fact]
    public async Task StocksRequest_ChecksComeOutPassFailOrNotAvailable() {
        // The web API builds the scored report from the reply item: present, zero and missing equity must give the
        // three check results (W1, B7 sign rule).
        JsonObject positive = FullReportJson();
        JsonObject zero = FullReportJson();
        zero["CurTotalShareholdersEquity"] = 0m;
        zero["RoeOwnerEarningsEquity"] = 0m;
        JsonObject missing = FullReportJson();
        missing["CurTotalShareholdersEquity"] = null;
        missing["RoeOwnerEarningsEquity"] = null;
        missing["RoeOwnerEarningsNumerator"] = null;
        SetupStocks(Dto("POS", positive.ToJsonString()), Dto("ZERO", zero.ToJsonString()), Dto("NONE", missing.ToJsonString()));

        GetStocksDataReply reply = await RequestStocks();

        Dictionary<string, CompanyFullDetailReport> reports = reply.StocksData
            .ToDictionary(i => i.CompanySymbol, CompanyFullDetailReport.FromReplyItem);
        reports["POS"].DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Pass);   // 845M / 2.31B = 0.37
        reports["ZERO"].DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
        reports["NONE"].DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.NotAvailable);
        reports["POS"].DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Pass); // 10.8%
        reports["ZERO"].DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
        reports["NONE"].DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.NotAvailable);
    }

    [Fact]
    public async Task StocksRequest_ReportWithoutPerYearRoe_HasNoYearsSoCheck16IsNotAvailable() {
        // A report written before C16 has no RoeOwnerEarningsByYear.
        JsonObject json = FullReportJson();
        _ = json.Remove("RoeOwnerEarningsByYear");

        GetStocksDataReplyItem item = await RequestSingleItem(json);

        item.RoeOwnerEarningsByYear.Should().BeEmpty();
        CompanyFullDetailReport.FromReplyItem(item).DoesPassCheck_ROE_FromOwnerEarnings_Consistent
            .Should().Be(CheckResult.NotAvailable);
    }

    [Fact]
    public async Task StocksRequest_MissingReportDates_LeavesThemUnset() {
        ProcessedFullInstrumentReportDto dto = Dto("NMR", FullReportJson().ToJsonString()) with {
            MostRecentAnnualReportDate = null, MostRecentQuarterlyReportDate = null, LastUpdatedDate = null
        };
        SetupStocks(dto);

        GetStocksDataReply reply = await RequestStocks();

        GetStocksDataReplyItem item = reply.StocksData.Should().ContainSingle().Subject;
        item.MostRecentAnnualReportDate.Should().BeNull();
        item.MostRecentQuarterlyReportDate.Should().BeNull();
        item.LastUpdatedDate.Should().BeNull();
        item.CreatedDate.ToDateTimeOffset().Should().Be(ReportCreated);
    }

    [Fact]
    public async Task StocksRequest_SerializedCompanyReport_IsServed() {
        // The real Aggregator format, not a hand-written one: guards the mapping against CompanyReport renames.
        var report = new CompanyReport("NMR", "Northern Maple Rail Corp", Tsx, curNumShares: 182_400_000);
        report.AddAnnualCashFlowStatement(new DateOnly(2025, 12, 31), TestDataFactory.CreateRawReport(
            ("NetIncomeFromContinuingOperations", 210_000_000m), ("ChangesInCash", 35_000_000m), ("CashDividendsPaid", 61_200_000m)));
        report.ProcessReports(new List<string>());
        report.CurTotalShareholdersEquity = 2_310_500_000m;
        report.CurLongTermDebt = 845_000_000m;
        report.CurShortTermDebt = 120_000_000m;
        SetupStocks(Dto("NMR", JsonSerializer.Serialize(report)));

        GetStocksDataReply reply = await RequestStocks();

        GetStocksDataReplyItem item = reply.StocksData.Should().ContainSingle().Subject;
        item.CurrentNumShares.Should().Be(182_400_000);
        item.CurrentTotalShareholdersEquity.Should().Be(2_310_500_000);
        item.CurrentLongTermDebt.Should().Be(845_000_000);
        item.CurrentShortTermDebt.Should().Be(120_000_000);
        item.CurrentDividendsPaid.Should().Be(61_200_000);
        item.AverageDividendsPaid.Should().Be(61_200_000);
        item.AverageNetCashFlow.Should().Be(35_000_000);
    }

    [Fact]
    public async Task StocksRequest_RowsWhoseReportDoesNotParse_AreSkippedAndTheRestServed() {
        JsonObject missingShareCount = FullReportJson();
        _ = missingShareCount.Remove("CurNumShares");
        JsonObject tooBig = FullReportJson();
        tooBig["AverageOwnerEarnings"] = decimal.MaxValue;
        SetupStocks(
            Dto("AAA", FullReportJson().ToJsonString()),
            Dto("BAD", "{ \"CurNumShares\": 1000, "),                                 // truncated JSON: TryParse throws
            Dto("MISS", missingShareCount.ToJsonString()),                             // TryParse returns false
            Dto("HUGE", tooBig.ToJsonString()),                                         // TryParse throws: overflows a long
            Dto("ZZZ", FullReportJson().ToJsonString()));

        GetStocksDataReply reply = await RequestStocks();

        reply.Success.Should().BeTrue();
        reply.StocksData.Select(i => i.CompanySymbol).Should().Equal("AAA", "ZZZ");
    }

    [Fact]
    public async Task StocksRequest_DbFailure_RepliesWithTheError() {
        _ = _dbm.Setup(d => d.GetProcessedStockDataByExchange(Tsx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>.SetFailure("connection refused"));

        GetStocksDataReply reply = await RequestStocks();

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("connection refused");
        reply.StocksData.Should().BeEmpty();
    }

    // ---- GetStocksDetail ----

    [Fact]
    public async Task DetailsRequest_Found_RepliesWithTheSameItemAsTheList() {
        ProcessedFullInstrumentReportDto dto = Dto("NMR", FullReportJson().ToJsonString());
        _ = _dbm.Setup(d => d.GetProcessedStockDataByExchangeAndSymbol(Tsx, "NMR.UN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessedFullInstrumentReportDto>.SetSuccess(dto));
        SetupStocks(dto);

        GetStocksDetailReply detail = await RequestDetail("NMR.UN");

        detail.Success.Should().BeTrue();
        detail.ErrorMessage.Should().BeEmpty();
        detail.StockDetail.Should().NotBeNull();
        detail.StockDetail.InstrumentSymbol.Should().Be("NMR.UN");
        detail.StockDetail.CurrentTotalShareholdersEquity.Should().Be(2_310_500_000);
        detail.StockDetail.RoeOwnerEarningsByYear.Should().HaveCount(5);

        // One mapping for both requests.
        using var listReq = new GetStocksForExchangeRequest(2, Tsx, null);
        _processor!.PostRequest(listReq).Should().BeTrue();
        var list = (GetStocksDataReply)(await listReq.Completed.Task.WaitAsync(Deadline))!;
        detail.StockDetail.Should().Be(list.StocksData.Single());
    }

    [Fact]
    public async Task DetailsRequest_ReportDoesNotParse_RepliesWithoutADetail() {
        ProcessedFullInstrumentReportDto dto = Dto("NMR", "not json");
        _ = _dbm.Setup(d => d.GetProcessedStockDataByExchangeAndSymbol(Tsx, "NMR.UN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessedFullInstrumentReportDto>.SetSuccess(dto));

        GetStocksDetailReply detail = await RequestDetail("NMR.UN");

        detail.StockDetail.Should().BeNull();
    }

    [Fact]
    public async Task DetailsRequest_NotFound_RepliesWithoutADetail() {
        // The DB call succeeds with no row (DbmService returns the statement's null report).
        _ = _dbm.Setup(d => d.GetProcessedStockDataByExchangeAndSymbol(Tsx, "NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Result<ProcessedFullInstrumentReportDto>(true, string.Empty, null));

        GetStocksDetailReply detail = await RequestDetail("NOPE");

        detail.StockDetail.Should().BeNull();
        // The "No data found" branch was reached only by DB failures (the branches were the wrong way round).
        detail.Success.Should().BeFalse();
        detail.ErrorMessage.Should().Be("No data found");
    }

    [Fact]
    public async Task DetailsRequest_DbFailure_RepliesWithAFailure() {
        _ = _dbm.Setup(d => d.GetProcessedStockDataByExchangeAndSymbol(Tsx, "NMR.UN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessedFullInstrumentReportDto>.SetFailure("connection refused"));

        GetStocksDetailReply detail = await RequestDetail("NMR.UN");

        detail.Success.Should().BeFalse();
        detail.ErrorMessage.Should().Be("connection refused", "a DB failure keeps its own error, not \"No data found\"");
        detail.StockDetail.Should().BeNull();
    }

    // ---- GetDashboardStats ----

    private static readonly DateTimeOffset RawIngestion = new(2026, 11, 22, 3, 41, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset Aggregation = new(2026, 11, 22, 3, 52, 30, TimeSpan.Zero);

    private void SetupDashboardStats() =>
        _dbm.Setup(d => d.GetDashboardStats(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, TestDataFactory.CreateDashboardStatsDto(
                totalActiveInstruments: 2_184,
                totalObsoletedInstruments: 317,
                instrumentsWithProcessedReports: 1_902,
                mostRecentRawIngestion: RawIngestion,
                mostRecentAggregation: Aggregation,
                unprocessedEventCount: 14,
                rawReportCountsByType: [new RawReportCountByTypeDto(1, 9_120), new RawReportCountByTypeDto(2, 9_087), new RawReportCountByTypeDto(3, 9_133)])));

    [Fact]
    public async Task DashboardStatsRequest_MapsEveryField() {
        SetupDashboardStats();
        // Saved times are timestamp without time zone, read back with an unspecified kind, and are UTC.
        _ = _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, new ApplicationCommonState(false,
                new DateTime(2026, 11, 22, 4, 41, 7, DateTimeKind.Unspecified),
                new DateTime(2026, 11, 22, 3, 45, 0, DateTimeKind.Unspecified),
                new DateTime(2026, 11, 22, 5, 0, 0, DateTimeKind.Unspecified),
                InstrumentKey.Empty)));

        GetDashboardStatsReply reply = await RequestDashboardStats();

        reply.Success.Should().BeTrue();
        reply.TotalActiveInstruments.Should().Be(2_184);
        reply.TotalObsoletedInstruments.Should().Be(317);
        reply.InstrumentsWithProcessedReports.Should().Be(1_902);
        reply.InstrumentsWithoutProcessedReports.Should().Be(282);
        reply.MostRecentRawIngestion.ToDateTimeOffset().Should().Be(RawIngestion);
        reply.MostRecentAggregation.ToDateTimeOffset().Should().Be(Aggregation);
        reply.UnprocessedEventCount.Should().Be(14);
        reply.RawReportCounts.Select(c => (c.ReportType, c.Count)).Should().Equal((1, 9_120L), (2, 9_087L), (3, 9_133L));
        reply.NextFetchDirectoryTime.ToDateTime().Should().Be(new DateTime(2026, 11, 22, 4, 41, 7, DateTimeKind.Utc));
        reply.NextFetchInstrumentDataTime.ToDateTime().Should().Be(new DateTime(2026, 11, 22, 3, 45, 0, DateTimeKind.Utc));
        reply.NextFetchQuotesTime.ToDateTime().Should().Be(new DateTime(2026, 11, 22, 5, 0, 0, DateTimeKind.Utc));
        reply.NextAggregatorCycleTime.Should().BeNull("no Aggregator is running");
    }

    [Fact]
    public async Task DashboardStatsRequest_NoSavedTimesOrDates_LeavesThemUnset() {
        _ = _dbm.Setup(d => d.GetDashboardStats(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, TestDataFactory.CreateDashboardStatsDto(totalActiveInstruments: 5)));
        _ = _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, new ApplicationCommonState()));

        GetDashboardStatsReply reply = await RequestDashboardStats();

        reply.Success.Should().BeTrue();
        reply.TotalActiveInstruments.Should().Be(5);
        reply.MostRecentRawIngestion.Should().BeNull();
        reply.MostRecentAggregation.Should().BeNull();
        reply.NextFetchDirectoryTime.Should().BeNull();
        reply.NextFetchInstrumentDataTime.Should().BeNull();
        reply.NextFetchQuotesTime.Should().BeNull();
        reply.RawReportCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task DashboardStatsRequest_AppStateReadFails_StillServesTheStats() {
        SetupDashboardStats();
        _ = _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("timeout"), (ApplicationCommonState?)null));

        GetDashboardStatsReply reply = await RequestDashboardStats();

        reply.Success.Should().BeTrue();
        reply.TotalActiveInstruments.Should().Be(2_184);
        reply.RawReportCounts.Should().HaveCount(3);
        reply.NextFetchDirectoryTime.Should().BeNull();
        reply.NextFetchInstrumentDataTime.Should().BeNull();
        reply.NextFetchQuotesTime.Should().BeNull();
    }

    [Fact]
    public async Task DashboardStatsRequest_StatsReadFails_RepliesWithTheErrorAndNoFigures() {
        _ = _dbm.Setup(d => d.GetDashboardStats(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("relation does not exist"), (DashboardStatsDto?)null));

        GetDashboardStatsReply reply = await RequestDashboardStats();

        reply.Success.Should().BeFalse();
        reply.ErrorMessage.Should().Be("relation does not exist");
        reply.TotalActiveInstruments.Should().Be(0);
        reply.RawReportCounts.Should().BeEmpty();
        _dbm.Verify(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()), Times.Never);
    }
}
