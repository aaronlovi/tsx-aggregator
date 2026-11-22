using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>
/// GetDashboardStatsStmt and GetRawReportCountsByTypeStmt (IDbmService.GetDashboardStats) against real PostgreSQL
/// (code review R58, R59: the B13 regression test in DashboardStatsCountTests only covered the in-memory fake).
/// Processed-report counts cover listed instruments with a current report; the other counts and latest dates.
/// </summary>
public sealed class DashboardStatsSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private static readonly DateTime T0 = new(2026, 11, 22, 7, 0, 0, DateTimeKind.Utc);

    private static InstrumentDto Instrument(long id, string symbol) =>
        new(id, TsxExchange, symbol, symbol + " Inc.", symbol, symbol + " Common", DateTimeOffset.UtcNow, null);

    private async Task<DashboardStatsDto> Stats() {
        (Result res, DashboardStatsDto? stats) = await db.Dbm.GetDashboardStats(CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return stats!;
    }

    [PostgresFact]
    public async Task EmptyDatabase_ZeroCountsAndNoDates() {
        await SqlTestRows.ClearAll(db);

        DashboardStatsDto stats = await Stats();

        stats.TotalActiveInstruments.Should().Be(0);
        stats.TotalObsoletedInstruments.Should().Be(0);
        stats.InstrumentsWithProcessedReports.Should().Be(0);
        stats.UnprocessedEventCount.Should().Be(0);
        stats.MostRecentRawIngestion.Should().BeNull();
        stats.MostRecentAggregation.Should().BeNull();
        stats.RawReportCountsByType.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task DelistedInstrumentWithProcessedReport_IsNotCounted() {
        // Mirrors DashboardStatsCountTests.GetDashboardStats_DelistedInstrumentWithProcessedReport_IsNotCounted (B13).
        await SqlTestRows.ClearAll(db);
        InstrumentDto listedWithReport = Instrument(1, "AAA");
        InstrumentDto listedWithoutReport = Instrument(2, "BBB");
        InstrumentDto delistedWithReport = Instrument(3, "CCC");
        _ = await db.Dbm.UpdateInstrumentList([listedWithReport, listedWithoutReport, delistedWithReport], [], [], CancellationToken.None);
        _ = await db.Dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(1, "{}", DateTimeOffset.UtcNow, null), CancellationToken.None);
        _ = await db.Dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(3, "{}", DateTimeOffset.UtcNow, null), CancellationToken.None);
        _ = await db.Dbm.UpdateInstrumentList([], [], [delistedWithReport], CancellationToken.None);

        DashboardStatsDto stats = await Stats();

        stats.TotalActiveInstruments.Should().Be(2);
        stats.TotalObsoletedInstruments.Should().Be(1);
        stats.InstrumentsWithProcessedReports.Should().Be(1);
        stats.InstrumentsWithoutProcessedReports.Should().Be(1);
    }

    [PostgresFact]
    public async Task ProcessedCount_IgnoresObsoletedReports_AndCountsEachInstrumentOnce() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddInstrument(db, 2, "BBB");
        await SqlTestRows.AddProcessedReport(db, 1, "{}", T0.AddDays(-2), obsoletedUtc: T0.AddDays(-1));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", T0.AddDays(-1));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", T0); // a second current row must not count twice
        await SqlTestRows.AddProcessedReport(db, 2, "{}", T0.AddDays(-2), obsoletedUtc: T0.AddDays(-1));

        DashboardStatsDto stats = await Stats();

        stats.InstrumentsWithProcessedReports.Should().Be(1);
        stats.InstrumentsWithoutProcessedReports.Should().Be(1);
    }

    [PostgresFact]
    public async Task UnprocessedEvents_LatestDates_AndCurrentRawReportCountsByType() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.NewListedCompany, isProcessed: true);
        await SqlTestRows.AddEvent(db, 1, T0.AddMinutes(1), CompanyEventTypes.RawDataChanged);
        await SqlTestRows.AddEvent(db, 1, T0.AddMinutes(2), CompanyEventTypes.RawDataChanged);
        await SqlTestRows.AddRawReport(db, 1, 1, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2024, 12, 31), createdUtc: T0.AddHours(-3));
        await SqlTestRows.AddRawReport(db, 2, 1, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31), createdUtc: T0.AddHours(-1));
        await SqlTestRows.AddRawReport(db, 3, 1, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2023, 12, 31), isCurrent: false, createdUtc: T0.AddHours(-5));
        await SqlTestRows.AddRawReport(db, 4, 1, ReportTypes.BalanceSheet, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31), createdUtc: T0.AddHours(-2));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", T0.AddMinutes(-30), obsoletedUtc: T0.AddMinutes(-10));
        await SqlTestRows.AddProcessedReport(db, 1, "{}", T0.AddMinutes(-10));

        DashboardStatsDto stats = await Stats();

        stats.UnprocessedEventCount.Should().Be(2);
        stats.MostRecentRawIngestion.Should().Be(new DateTimeOffset(T0.AddHours(-1)));
        stats.MostRecentAggregation.Should().Be(new DateTimeOffset(T0.AddMinutes(-10)));
        stats.RawReportCountsByType.Should().Equal(
            new RawReportCountByTypeDto((int)ReportTypes.CashFlow, 2),
            new RawReportCountByTypeDto((int)ReportTypes.BalanceSheet, 1));
    }
}
