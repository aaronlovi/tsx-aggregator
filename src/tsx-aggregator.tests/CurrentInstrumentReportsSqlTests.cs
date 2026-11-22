using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>
/// IDbmService.GetCurrentInstrumentReports (Aggregator) and GetRawFinancialsByInstrumentId (RawCollector) against real
/// PostgreSQL (code review R55): only the current raw reports of the one instrument, with every field mapped, and the
/// two methods returning the same reports.
/// </summary>
public sealed class CurrentInstrumentReportsSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private const long InstrumentId = 5;
    private const long OtherInstrumentId = 6;
    private const string BalanceSheetJson = "{\"TotalAssets\":98000000}";
    private const string QuarterlyCashFlowJson = "{\"CashFlowFromOperations\":4200000}";

    private async Task Reset() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, InstrumentId, "ABC");
        await SqlTestRows.AddInstrument(db, OtherInstrumentId, "XYZ");
        // Report type and period type values differ in each report (and between the two), so a swapped column shows.
        await SqlTestRows.AddRawReport(db, 101, InstrumentId, ReportTypes.BalanceSheet, ReportPeriodTypes.Annual, new DateOnly(2024, 12, 31), reportJson: BalanceSheetJson);
        await SqlTestRows.AddRawReport(db, 102, InstrumentId, ReportTypes.CashFlow, ReportPeriodTypes.Quarterly, new DateOnly(2025, 6, 30), reportJson: QuarterlyCashFlowJson);
        // Not returned: an obsoleted report of the instrument, and another instrument's current report.
        await SqlTestRows.AddRawReport(db, 103, InstrumentId, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2023, 12, 31), isCurrent: false);
        await SqlTestRows.AddRawReport(db, 201, OtherInstrumentId, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2024, 12, 31));
    }

    private static readonly CurrentInstrumentRawDataReportDto[] Expected = [
        new(101, InstrumentId, (int)ReportTypes.BalanceSheet, (int)ReportPeriodTypes.Annual, BalanceSheetJson, new DateOnly(2024, 12, 31)),
        new(102, InstrumentId, (int)ReportTypes.CashFlow, (int)ReportPeriodTypes.Quarterly, QuarterlyCashFlowJson, new DateOnly(2025, 6, 30)),
    ];

    [PostgresFact]
    public async Task GetCurrentInstrumentReports_ReturnsOnlyTheCurrentReportsOfTheInstrument_WithEveryField() {
        await Reset();

        (Result res, IReadOnlyList<CurrentInstrumentRawDataReportDto> reports) = await db.Dbm.GetCurrentInstrumentReports(InstrumentId, CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        reports.Should().BeEquivalentTo(Expected);
    }

    [PostgresFact]
    public async Task GetRawFinancialsByInstrumentId_ReturnsOnlyTheCurrentReportsOfTheInstrument_WithEveryField() {
        await Reset();

        (Result res, IReadOnlyList<CurrentInstrumentRawDataReportDto> reports) = await db.Dbm.GetRawFinancialsByInstrumentId(InstrumentId, CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        reports.Should().BeEquivalentTo(Expected);
    }

    [PostgresFact]
    public async Task BothMethods_UnknownInstrument_SucceedWithNoReports() {
        await Reset();

        (Result res1, IReadOnlyList<CurrentInstrumentRawDataReportDto> reports1) = await db.Dbm.GetCurrentInstrumentReports(999, CancellationToken.None);
        (Result res2, IReadOnlyList<CurrentInstrumentRawDataReportDto> reports2) = await db.Dbm.GetRawFinancialsByInstrumentId(999, CancellationToken.None);

        res1.Success.Should().BeTrue(res1.ErrMsg);
        res2.Success.Should().BeTrue(res2.ErrMsg);
        reports1.Should().BeEmpty();
        reports2.Should().BeEmpty();
    }
}
