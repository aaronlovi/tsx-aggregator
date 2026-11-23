using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;
using static tsx_aggregator.tests.TestDataFactory;

namespace tsx_aggregator.tests;

/// <summary>
/// The current debt figures of <see cref="CompanyReport"/> (code review X4, EDGAR E9): a long-term debt figure missing
/// from the latest balance sheet, usually an interim one, comes from the latest complete annual balance sheet.
/// Measured in the review's section 3.6: debt for 21 companies, no score change.
/// </summary>
public class CompanyReportDebtTests {
    private static readonly DateOnly Fy2021 = new(2021, 12, 31);
    private static readonly DateOnly Q1Fy2022 = new(2022, 3, 31);

    private static CompanyReport NewReport() => new("TST", "Test Co", "TSX", 1000);

    private static List<string> Process(CompanyReport report) {
        var warnings = new List<string>();
        report.ProcessReports(warnings);
        return warnings;
    }

    private static void AddAnnualSet(CompanyReport report, DateOnly date, params (string Key, decimal Value)[] balanceSheet) {
        report.AddAnnualBalanceSheet(date, CreateRawReport([("StockholdersEquity", 1000m), .. balanceSheet]));
        report.AddAnnualCashFlowStatement(date, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        report.AddAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
    }

    private static void AddQuarterSet(CompanyReport report, DateOnly date, params (string Key, decimal Value)[] balanceSheet) {
        report.AddNonAnnualBalanceSheet(date, CreateRawReport([("StockholdersEquity", 1100m), .. balanceSheet]));
        report.AddNonAnnualCashFlowStatement(date, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        report.AddNonAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
    }

    [Fact]
    public void QuarterWithoutLongTermDebt_TakesTheAnnualFigure() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, ("LongTermDebt", 100m), ("LongTermDebtAndCapitalLeaseObligation", 300m));
        AddQuarterSet(report, Q1Fy2022);

        List<string> warnings = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(1100m, "the other figures still come from the quarter");
        _ = report.CurLongTermDebt.Should().Be(300m, "the annual sheet's figure, in the usual key order");
        _ = warnings.Should().Contain($"Most recent figures: current LongTermDebt from the annual balance sheet of {Fy2021}");
        _ = warnings.Should().NotContain("Most recent figures: missing property current LongTermDebt");
    }

    [Fact]
    public void QuarterWithLongTermDebt_KeepsItsOwnFigure() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, ("LongTermDebt", 300m));
        AddQuarterSet(report, Q1Fy2022, ("LongTermDebt", 250m));

        _ = Process(report);

        _ = report.CurLongTermDebt.Should().Be(250m);
    }

    [Fact]
    public void QuarterWithZeroLongTermDebt_KeepsZero() {
        // A reported 0 is a figure, not a gap.
        var report = NewReport();
        AddAnnualSet(report, Fy2021, ("LongTermDebt", 300m));
        AddQuarterSet(report, Q1Fy2022, ("LongTermDebt", 0m));

        _ = Process(report);

        _ = report.CurLongTermDebt.Should().Be(0m);
    }

    [Fact]
    public void NoLongTermDebtAnywhere_IsZeroAndWarns() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021);
        AddQuarterSet(report, Q1Fy2022);

        List<string> warnings = Process(report);

        _ = report.CurLongTermDebt.Should().Be(0m);
        _ = warnings.Should().Contain("Most recent figures: missing property current LongTermDebt");
    }

    [Fact]
    public void IncompleteAnnualSet_IsNotUsed() {
        // Only an annual balance sheet with its cash flow and income statement counts, as for the other figures.
        var report = NewReport();
        AddAnnualSet(report, new DateOnly(2020, 12, 31));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("StockholdersEquity", 1000m), ("LongTermDebt", 300m)));
        AddQuarterSet(report, Q1Fy2022);

        _ = Process(report);

        _ = report.CurLongTermDebt.Should().Be(0m);
    }

    // X3: debt due within a year, same key order as the long-term chain (leases on both sides or neither), with the
    // same annual fallback. Measured in the review's section 3.7.

    [Fact]
    public void ShortTermDebt_PrefersIncludingLeases() {
        var report = NewReport();
        AddQuarterSet(report, Q1Fy2022, ("CurrentDebt", 40m), ("CurrentDebtAndCapitalLeaseObligation", 55m));

        _ = Process(report);

        _ = report.CurShortTermDebt.Should().Be(55m);
    }

    [Fact]
    public void ShortTermDebt_FallsBackToCurrentDebt() {
        var report = NewReport();
        AddQuarterSet(report, Q1Fy2022, ("CurrentDebt", 40m));

        _ = Process(report);

        _ = report.CurShortTermDebt.Should().Be(40m);
    }

    [Fact]
    public void QuarterWithoutShortTermDebt_TakesTheAnnualFigure() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, ("CurrentDebt", 70m), ("LongTermDebt", 300m));
        AddQuarterSet(report, Q1Fy2022, ("LongTermDebt", 250m));

        List<string> warnings = Process(report);

        _ = report.CurShortTermDebt.Should().Be(70m);
        _ = report.CurLongTermDebt.Should().Be(250m, "each part falls back on its own");
        _ = warnings.Should().Contain($"Most recent figures: current ShortTermDebt from the annual balance sheet of {Fy2021}");
    }

    [Fact]
    public void QuarterWithZeroShortTermDebt_KeepsZero() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, ("CurrentDebt", 70m));
        AddQuarterSet(report, Q1Fy2022, ("CurrentDebt", 0m));

        _ = Process(report);

        _ = report.CurShortTermDebt.Should().Be(0m);
    }

    [Fact]
    public void NoShortTermDebtAnywhere_IsZeroWithoutAWarning() {
        // Most companies without the key simply have no debt due within a year.
        var report = NewReport();
        AddAnnualSet(report, Fy2021);
        AddQuarterSet(report, Q1Fy2022);

        List<string> warnings = Process(report);

        _ = report.CurShortTermDebt.Should().Be(0m);
        _ = warnings.Should().NotContain(w => w.Contains("ShortTermDebt"));
    }

    // R94: dividends averaged over the same annual statements as the average net cash flow.

    [Fact]
    public void AverageDividendsPaid_AveragesTheSameYearsAsNetCashFlow() {
        // TBL-like: a special dividend in the latest year. Years without the key count as 0, as in net cash flow.
        var report = NewReport();
        decimal?[] dividends = [30m, null, 25m, null, 180m];
        for (int i = 0; i < dividends.Length; i++) {
            var date = new DateOnly(2021 + i, 12, 31);
            report.AddAnnualCashFlowStatement(date, dividends[i] is decimal d
                ? CreateRawReport(("NetIncomeFromContinuingOperations", 10m), ("ChangesInCash", 5m), ("CashDividendsPaid", d))
                : CreateRawReport(("NetIncomeFromContinuingOperations", 10m), ("ChangesInCash", 5m)));
        }

        _ = Process(report);

        _ = report.CurDividendsPaid.Should().Be(180m, "the latest year, still shown on the page");
        _ = report.AverageDividendsPaid.Should().Be(47m, "(30 + 0 + 25 + 0 + 180) / 5");
        _ = report.NumAnnualProcessedCashFlowReports.Should().Be(5);
    }

    [Fact]
    public void AverageDividendsPaid_NoCashFlowStatements_IsNull() {
        var report = NewReport();
        AddQuarterSet(report, Q1Fy2022);

        _ = Process(report);

        _ = report.AverageDividendsPaid.Should().BeNull();
    }

    [Fact]
    public void LatestCompleteAnnualSheet_IsTheOneUsed() {
        var report = NewReport();
        AddAnnualSet(report, new DateOnly(2020, 12, 31), ("LongTermDebt", 200m));
        AddAnnualSet(report, Fy2021, ("LongTermDebt", 300m));
        AddQuarterSet(report, Q1Fy2022);

        _ = Process(report);

        _ = report.CurLongTermDebt.Should().Be(300m);
    }
}
