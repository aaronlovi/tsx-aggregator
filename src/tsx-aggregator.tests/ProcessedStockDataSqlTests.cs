using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>
/// GetProcessedStockDataByExchangeStmt and GetProcessedStockDataByExchangeAndInstrumentSymbolStmt against real
/// PostgreSQL (code review R58): only current processed reports of listed instruments, the join to the current annual
/// cash-flow reports (count and latest report dates), exchange filtering and an unknown symbol.
/// </summary>
public sealed class ProcessedStockDataSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private static readonly DateTime ReportCreated = new(2026, 11, 20, 8, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime LastScraped = new(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc);
    private long _nextReportId = 1000;

    /// <summary>A listed instrument with one current processed report and one current annual cash-flow report.</summary>
    private async Task AddCompany(long instrumentId, string symbol, string exchange = TsxExchange, DateTime? obsoletedUtc = null) {
        await SqlTestRows.AddInstrument(db, instrumentId, symbol, exchange, obsoletedUtc, LastScraped);
        await SqlTestRows.AddRawReport(db, _nextReportId++, instrumentId, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31));
        await SqlTestRows.AddProcessedReport(db, instrumentId, $"{{\"symbol\":\"{symbol}\"}}", ReportCreated);
    }

    private async Task<IReadOnlyList<ProcessedFullInstrumentReportDto>> ByExchange(string exchange = TsxExchange) {
        Result<IReadOnlyList<ProcessedFullInstrumentReportDto>> res = await db.Dbm.GetProcessedStockDataByExchange(exchange, CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return res.Data!;
    }

    private async Task<ProcessedFullInstrumentReportDto?> BySymbol(string symbol, string exchange = TsxExchange) {
        Result<ProcessedFullInstrumentReportDto> res = await db.Dbm.GetProcessedStockDataByExchangeAndSymbol(exchange, symbol, CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return res.Data;
    }

    [PostgresFact]
    public async Task ByExchange_ReturnsOnlyTheCurrentProcessedReport() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "AAA");
        await SqlTestRows.AddProcessedReport(db, 1, "{\"old\":true}", ReportCreated.AddDays(-7), obsoletedUtc: ReportCreated);

        IReadOnlyList<ProcessedFullInstrumentReportDto> rows = await ByExchange();

        rows.Should().ContainSingle();
        rows[0].SerializedReport.Should().Be("{\"symbol\":\"AAA\"}");
        rows[0].ReportCreatedDate.Should().Be(new DateTimeOffset(ReportCreated));
    }

    [PostgresFact]
    public async Task ByExchange_LeavesOutDelistedInstruments_AndOtherExchanges() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "AAA");
        await AddCompany(2, "GONE", obsoletedUtc: ReportCreated.AddDays(-1));
        await AddCompany(3, "VEN", exchange: "TSXV");

        IReadOnlyList<ProcessedFullInstrumentReportDto> rows = await ByExchange();

        rows.Select(r => r.InstrumentSymbol).Should().BeEquivalentTo(["AAA"]);
        (await ByExchange("TSXV")).Select(r => r.InstrumentSymbol).Should().BeEquivalentTo(["VEN"]);
    }

    [PostgresFact]
    public async Task ByExchange_LeavesOutAnInstrumentWithOnlyAnObsoletedProcessedReport() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA", lastScrapedUtc: LastScraped);
        await SqlTestRows.AddRawReport(db, 1, 1, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", ReportCreated.AddDays(-7), obsoletedUtc: ReportCreated);

        (await ByExchange()).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task ByExchange_MapsInstrumentFields_CountsCurrentAnnualCashFlowReports_AndLatestReportDates() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 7, "RY", lastScrapedUtc: LastScraped);
        await SqlTestRows.AddProcessedReport(db, 7, "{\"score\":15}", ReportCreated);
        await SqlTestRows.AddRawReport(db, 1, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2023, 10, 31));
        await SqlTestRows.AddRawReport(db, 2, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2024, 10, 31));
        await SqlTestRows.AddRawReport(db, 3, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 10, 31));
        // Not counted: an obsoleted annual cash flow (with the latest date), an annual income statement, a quarterly cash flow.
        await SqlTestRows.AddRawReport(db, 4, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2026, 10, 31), isCurrent: false);
        await SqlTestRows.AddRawReport(db, 5, 7, ReportTypes.IncomeStatement, ReportPeriodTypes.Annual, new DateOnly(2025, 10, 31));
        await SqlTestRows.AddRawReport(db, 6, 7, ReportTypes.CashFlow, ReportPeriodTypes.Quarterly, new DateOnly(2026, 7, 31));
        await SqlTestRows.AddRawReport(db, 8, 7, ReportTypes.CashFlow, ReportPeriodTypes.Quarterly, new DateOnly(2026, 10, 31), isCurrent: false);

        ProcessedFullInstrumentReportDto row = (await ByExchange()).Should().ContainSingle().Subject;

        row.InstrumentId.Should().Be(7);
        row.CompanySymbol.Should().Be("RY");
        row.CompanyName.Should().Be("RY Inc.");
        row.InstrumentSymbol.Should().Be("RY");
        row.InstrumentName.Should().Be("RY Common Shares");
        row.SerializedReport.Should().Be("{\"score\":15}");
        row.InstrumentCreatedDate.Should().Be(new DateTimeOffset(SqlTestRows.Created));
        row.ReportCreatedDate.Should().Be(new DateTimeOffset(ReportCreated));
        row.NumAnnualCashFlowReports.Should().Be(3);
        row.MostRecentAnnualReportDate.Should().Be(new DateTimeOffset(2025, 10, 31, 0, 0, 0, TimeSpan.Zero));
        row.MostRecentQuarterlyReportDate.Should().Be(new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero));
        row.LastUpdatedDate.Should().Be(new DateTimeOffset(LastScraped));
    }

    [PostgresFact]
    public async Task ByExchange_ReturnsEveryListedCompany() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "AAA");
        await AddCompany(2, "BBB");
        await AddCompany(3, "CCC");

        (await ByExchange()).Select(r => r.InstrumentSymbol).Should().BeEquivalentTo(["AAA", "BBB", "CCC"]);
    }

    [PostgresFact]
    public async Task BySymbol_ReturnsTheCurrentReportOfThatSymbol() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "AAA");
        await AddCompany(2, "BBB");

        ProcessedFullInstrumentReportDto? row = await BySymbol("BBB");

        row.Should().NotBeNull();
        row!.InstrumentId.Should().Be(2);
        row.SerializedReport.Should().Be("{\"symbol\":\"BBB\"}");
        row.NumAnnualCashFlowReports.Should().Be(1);
        row.LastUpdatedDate.Should().Be(new DateTimeOffset(LastScraped));
    }

    [PostgresFact]
    public async Task BySymbol_MapsEveryFieldLikeByExchange() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 7, "RY", lastScrapedUtc: LastScraped);
        await SqlTestRows.AddProcessedReport(db, 7, "{\"score\":15}", ReportCreated);
        await SqlTestRows.AddRawReport(db, 1, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2024, 10, 31));
        await SqlTestRows.AddRawReport(db, 2, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 10, 31));
        await SqlTestRows.AddRawReport(db, 3, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2026, 10, 31), isCurrent: false);
        await SqlTestRows.AddRawReport(db, 4, 7, ReportTypes.CashFlow, ReportPeriodTypes.Quarterly, new DateOnly(2026, 7, 31));
        await AddCompany(8, "TD");

        ProcessedFullInstrumentReportDto? row = await BySymbol("RY");

        row.Should().Be(new ProcessedFullInstrumentReportDto(
            InstrumentId: 7,
            CompanySymbol: "RY",
            CompanyName: "RY Inc.",
            InstrumentSymbol: "RY",
            InstrumentName: "RY Common Shares",
            SerializedReport: "{\"score\":15}",
            InstrumentCreatedDate: new DateTimeOffset(SqlTestRows.Created),
            InstrumentObsoletedDate: null,
            ReportCreatedDate: new DateTimeOffset(ReportCreated),
            ReportObsoletedDate: null,
            NumAnnualCashFlowReports: 2,
            MostRecentAnnualReportDate: new DateTimeOffset(2025, 10, 31, 0, 0, 0, TimeSpan.Zero),
            MostRecentQuarterlyReportDate: new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero),
            LastUpdatedDate: new DateTimeOffset(LastScraped)));
        (await ByExchange()).Should().ContainEquivalentOf(row);
    }

    [PostgresFact]
    public async Task BySymbol_UnknownSymbol_SucceedsWithNoData() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "AAA");

        (await BySymbol("ZZZ")).Should().BeNull();
    }

    [PostgresFact]
    public async Task BySymbol_OnAnotherExchange_IsNotFound() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "VEN", exchange: "TSXV");

        (await BySymbol("VEN")).Should().BeNull();
        (await BySymbol("VEN", "TSXV")).Should().NotBeNull();
    }

    [PostgresFact]
    public async Task BySymbol_DelistedInstrument_IsNotFound() {
        await SqlTestRows.ClearAll(db);
        await AddCompany(1, "GONE", obsoletedUtc: ReportCreated.AddDays(-1));

        (await BySymbol("GONE")).Should().BeNull();
    }

    [PostgresFact]
    public async Task BySymbol_WithOnlyAnObsoletedProcessedReport_IsNotFound() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddRawReport(db, 1, 1, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", ReportCreated.AddDays(-7), obsoletedUtc: ReportCreated);

        (await BySymbol("AAA")).Should().BeNull();
    }
}
