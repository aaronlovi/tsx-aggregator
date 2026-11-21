using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

using static tsx_aggregator.tests.TestDataFactory;

namespace tsx_aggregator.tests;

/// <summary>
/// Tests for <see cref="CompanyReport"/> processing, through its public surface
/// (CashFlowItem is internal). Fixtures follow the real raw-data sign conventions
/// (Scoring Bug Fix Plan, section 2).
/// </summary>
public class CompanyReportTests {
    private static readonly DateOnly Fy2020 = new(2020, 12, 31);
    private static readonly DateOnly Fy2021 = new(2021, 12, 31);

    private static CompanyReport NewReport() => new("TST", "Test Co", "TSX", 1000);

    private static CompanyReport Process(CompanyReport report) {
        report.ProcessReports(new List<string>());
        return report;
    }

    #region B3 - debt fallback

    [Fact]
    public void NetCashFlow_NoDebtCashFlowKeys_FallsBackToBalanceSheetDebtChange() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 200m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("LongTermDebt", 100m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("LongTermDebt", 150m)));

        _ = Process(report);

        // Debt went 100 -> 150: 50 of the cash came from issuing debt, so it is removed.
        _ = report.AverageNetCashFlow.Should().Be(150m);
    }

    [Fact]
    public void NetCashFlow_NoDebtCashFlowKeys_DebtRepaid_AddsRepaymentBack() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 200m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("LongTermDebtAndCapitalLeaseObligation", 150m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("LongTermDebtAndCapitalLeaseObligation", 100m)));

        _ = Process(report);

        _ = report.AverageNetCashFlow.Should().Be(250m);
    }

    [Fact]
    public void NetCashFlow_DebtCashFlowKeyPresent_IgnoresBalanceSheets() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 200m),
            ("NetIssuancePaymentsOfDebt", 30m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("LongTermDebt", 100m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("LongTermDebt", 150m)));

        _ = Process(report);

        _ = report.AverageNetCashFlow.Should().Be(170m);
    }

    [Fact]
    public void NetCashFlow_NoDebtKeysAnywhere_NoDebtAdjustment() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 200m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("LongTermDebt", 100m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("CurrentAssets", 10m)));

        _ = Process(report);

        _ = report.AverageNetCashFlow.Should().Be(200m);
    }

    #endregion
}
