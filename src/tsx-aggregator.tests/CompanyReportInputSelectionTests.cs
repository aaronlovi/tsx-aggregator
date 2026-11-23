using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;
using static tsx_aggregator.tests.TestDataFactory;

namespace tsx_aggregator.tests;

/// <summary>
/// Characterization tests for how <see cref="CompanyReport"/> picks its inputs: which report set the current and
/// oldest balance sheet figures come from, and the key fallbacks for each figure (reviews R65 and R66).
/// </summary>
public class CompanyReportInputSelectionTests {
    private static readonly DateOnly Fy2020 = new(2020, 12, 31);
    private static readonly DateOnly Fy2021 = new(2021, 12, 31);
    private static readonly DateOnly Q1Fy2021 = new(2021, 3, 31);
    private static readonly DateOnly Q1Fy2022 = new(2022, 3, 31);

    private static CompanyReport NewReport() => new("TST", "Test Co", "TSX", 1000);

    private static List<string> Process(CompanyReport report) {
        var warnings = new List<string>();
        report.ProcessReports(warnings);
        return warnings;
    }

    private static void AddAnnualSet(CompanyReport report, DateOnly date, decimal equity, decimal retainedEarnings) {
        report.AddAnnualBalanceSheet(date, CreateRawReport(("StockholdersEquity", equity), ("RetainedEarnings", retainedEarnings)));
        report.AddAnnualCashFlowStatement(date, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        report.AddAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
    }

    private static void AddNonAnnualSet(CompanyReport report, DateOnly date, decimal equity, decimal retainedEarnings) {
        report.AddNonAnnualBalanceSheet(date, CreateRawReport(("StockholdersEquity", equity), ("RetainedEarnings", retainedEarnings)));
        report.AddNonAnnualCashFlowStatement(date, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        report.AddNonAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
    }

    #region R65 - report set selection

    [Fact]
    public void MostRecent_LaterQuarterBeatsAnnual() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, equity: 100m, retainedEarnings: 10m);
        AddNonAnnualSet(report, Q1Fy2022, equity: 200m, retainedEarnings: 20m);

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(200m);
        _ = report.OldestRetainedEarnings.Should().Be(10m);
    }

    [Fact]
    public void MostRecent_LaterAnnualBeatsQuarter() {
        var report = NewReport();
        AddNonAnnualSet(report, Q1Fy2021, equity: 200m, retainedEarnings: 20m);
        AddAnnualSet(report, Fy2021, equity: 100m, retainedEarnings: 10m);

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(100m);
        _ = report.OldestRetainedEarnings.Should().Be(20m);
    }

    [Fact]
    public void SameDateAnnualAndQuarter_AnnualWinsForMostRecentAndOldest() {
        var report = NewReport();
        AddNonAnnualSet(report, Fy2021, equity: 200m, retainedEarnings: 20m);
        AddAnnualSet(report, Fy2021, equity: 100m, retainedEarnings: 10m);

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(100m);
        _ = report.OldestRetainedEarnings.Should().Be(10m);
    }

    [Fact]
    public void MostRecentAndOldest_PickTheLatestAndEarliestOfSeveralQuarters() {
        var report = NewReport();
        AddNonAnnualSet(report, new DateOnly(2021, 3, 31), equity: 100m, retainedEarnings: 1m);
        AddNonAnnualSet(report, new DateOnly(2021, 6, 30), equity: 200m, retainedEarnings: 2m);
        AddNonAnnualSet(report, new DateOnly(2021, 9, 30), equity: 300m, retainedEarnings: 3m);

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(300m);
        _ = report.CurRetainedEarnings.Should().Be(3m);
        _ = report.OldestRetainedEarnings.Should().Be(1m);
    }

    [Fact]
    public void IncompleteSets_AreSkipped_AndOnlyTheOldestSearchWarns() {
        var report = NewReport();
        // Incomplete annual sets before and after the complete one.
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("StockholdersEquity", 50m), ("RetainedEarnings", 5m)));
        report.AddAnnualIncomeStatement(Fy2020, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
        AddAnnualSet(report, Fy2021, equity: 100m, retainedEarnings: 10m);
        var fy2022 = new DateOnly(2022, 12, 31);
        report.AddAnnualBalanceSheet(fy2022, CreateRawReport(("StockholdersEquity", 300m), ("RetainedEarnings", 30m)));
        report.AddAnnualCashFlowStatement(fy2022, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        // Incomplete quarters: one without anything else, one without an income statement.
        report.AddNonAnnualBalanceSheet(Q1Fy2021, CreateRawReport(("StockholdersEquity", 400m), ("RetainedEarnings", 40m)));
        report.AddNonAnnualBalanceSheet(Q1Fy2022, CreateRawReport(("StockholdersEquity", 500m), ("RetainedEarnings", 50m)));
        report.AddNonAnnualCashFlowStatement(Q1Fy2022, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));

        List<string> warnings = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(100m);
        _ = report.OldestRetainedEarnings.Should().Be(10m);
        _ = warnings.Where(w => w.Contains("but no matching")).Should().Equal(
            $"Non-annual balance sheet for {Q1Fy2021} but no matching cash flow statement",
            $"Non-annual balance sheet for {Q1Fy2021} but no matching income statement",
            $"Non-annual balance sheet for {Q1Fy2022} but no matching income statement",
            $"Annual balance sheet for {Fy2020} but no matching cash flow statement",
            $"Annual balance sheet for {fy2022} but no matching income statement");
    }

    [Fact]
    public void NoCompleteSet_LeavesCurrentAndOldestFiguresUnset() {
        var report = NewReport();
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("StockholdersEquity", 100m), ("RetainedEarnings", 10m)));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().BeNull();
        _ = report.CurRetainedEarnings.Should().BeNull();
        _ = report.OldestRetainedEarnings.Should().BeNull();
    }

    [Fact]
    public void AddByPeriodType_RoutesAnnualQuarterlyAndSemiAnnual_IgnoresUndefined() {
        var annual = NewReport();
        annual.AddBalanceSheet(ReportPeriodTypes.Annual, Fy2021, CreateRawReport(("StockholdersEquity", 100m)));
        annual.AddCashFlowStatement(ReportPeriodTypes.Annual, Fy2021, CreateRawReport(("ChangesInCash", 7m)));
        annual.AddIncomeStatement(ReportPeriodTypes.Annual, Fy2021, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
        _ = Process(annual);
        _ = annual.CurTotalShareholdersEquity.Should().Be(100m);
        _ = annual.NumAnnualProcessedCashFlowReports.Should().Be(1);

        foreach (ReportPeriodTypes nonAnnualType in new[] { ReportPeriodTypes.Quarterly, ReportPeriodTypes.SemiAnnual }) {
            var report = NewReport();
            report.AddBalanceSheet(nonAnnualType, Fy2021, CreateRawReport(("StockholdersEquity", 100m)));
            report.AddCashFlowStatement(nonAnnualType, Fy2021, CreateRawReport(("ChangesInCash", 7m)));
            report.AddIncomeStatement(nonAnnualType, Fy2021, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
            _ = Process(report);
            _ = report.CurTotalShareholdersEquity.Should().Be(100m);
            _ = report.NumAnnualProcessedCashFlowReports.Should().Be(0);
        }

        var undefined = NewReport();
        undefined.AddBalanceSheet(ReportPeriodTypes.Undefined, Fy2021, CreateRawReport(("StockholdersEquity", 100m)));
        undefined.AddCashFlowStatement(ReportPeriodTypes.Undefined, Fy2021, CreateRawReport(("ChangesInCash", 7m)));
        undefined.AddIncomeStatement(ReportPeriodTypes.Undefined, Fy2021, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
        _ = Process(undefined);
        _ = undefined.CurTotalShareholdersEquity.Should().BeNull();
        _ = undefined.NumAnnualProcessedCashFlowReports.Should().Be(0);
    }

    [Fact]
    public void AddingASecondReportForTheSameDate_KeepsTheFirst() {
        var report = NewReport();
        AddAnnualSet(report, Fy2021, equity: 100m, retainedEarnings: 10m);
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("StockholdersEquity", 999m)));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("ChangesInCash", 999m)));
        report.AddNonAnnualBalanceSheet(Q1Fy2022, CreateRawReport(("StockholdersEquity", 200m)));
        report.AddNonAnnualBalanceSheet(Q1Fy2022, CreateRawReport(("StockholdersEquity", 999m)));
        report.AddNonAnnualCashFlowStatement(Q1Fy2022, CreateRawReport(("ChangesInCash", 1m)));
        report.AddNonAnnualCashFlowStatement(Q1Fy2022, CreateRawReport(("ChangesInCash", 2m)));
        report.AddNonAnnualIncomeStatement(Q1Fy2022, CreateRawReport(("NetIncomeContinuousOperations", 1m)));
        report.AddNonAnnualIncomeStatement(Q1Fy2022, CreateRawReport(("NetIncomeContinuousOperations", 2m)));

        _ = Process(report);

        _ = report.CurTotalShareholdersEquity.Should().Be(200m);
        _ = report.OldestRetainedEarnings.Should().Be(10m);
        // The first annual cash flow has no ChangesInCash, so its net cash flow is 0.
        _ = report.AverageNetCashFlow.Should().Be(0m);
    }

    #endregion

    #region R66 - key fallbacks

    private static (CompanyReport Report, List<string> Warnings) WithCurrentBalanceSheet(params (string Key, decimal Value)[] fields) {
        var report = NewReport();
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(fields));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("NetIncomeFromContinuingOperations", 10m)));
        report.AddAnnualIncomeStatement(Fy2021, CreateRawReport(("NetIncomeContinuousOperations", 10m)));
        return (report, Process(report));
    }

    [Fact]
    public void Goodwill_PrefersGoodwill() {
        (CompanyReport report, _) = WithCurrentBalanceSheet(
            ("Goodwill", 30m), ("GoodwillAndOtherIntangibleAssets", 100m), ("OtherIntangibleAssets", 20m));
        _ = report.CurGoodwill.Should().Be(30m);
        _ = report.CurIntangibles.Should().Be(20m);
    }

    [Fact]
    public void Goodwill_CombinedLessIntangibles() {
        (CompanyReport report, _) = WithCurrentBalanceSheet(
            ("GoodwillAndOtherIntangibleAssets", 100m), ("OtherIntangibleAssets", 20m));
        _ = report.CurGoodwill.Should().Be(80m);
        _ = report.CurIntangibles.Should().Be(20m);
    }

    [Fact]
    public void Goodwill_CombinedAlone_AllGoodwill_IntangiblesMissing() {
        (CompanyReport report, List<string> warnings) = WithCurrentBalanceSheet(("GoodwillAndOtherIntangibleAssets", 100m));
        _ = report.CurGoodwill.Should().Be(100m);
        _ = report.CurIntangibles.Should().Be(0m);
        _ = warnings.Should().Contain("Most recent figures: missing property current Intangibles");
        _ = warnings.Should().NotContain("TransformMostRecentFinancialReports: missing property current Goodwill");
    }

    [Fact]
    public void Intangibles_CombinedLessGoodwill() {
        (CompanyReport report, _) = WithCurrentBalanceSheet(
            ("GoodwillAndOtherIntangibleAssets", 100m), ("Goodwill", 30m));
        _ = report.CurGoodwill.Should().Be(30m);
        _ = report.CurIntangibles.Should().Be(70m);
    }

    [Fact]
    public void GoodwillAndIntangibles_Missing_AreZeroAndWarn() {
        (CompanyReport report, List<string> warnings) = WithCurrentBalanceSheet(("StockholdersEquity", 500m));
        _ = report.CurGoodwill.Should().Be(0m);
        _ = report.CurIntangibles.Should().Be(0m);
        _ = report.CurLongTermDebt.Should().Be(0m);
        _ = report.CurBookValue.Should().Be(500m);
        _ = warnings.Should().Contain("TransformMostRecentFinancialReports: missing property current Goodwill");
        _ = warnings.Should().Contain("Most recent figures: missing property current Intangibles");
        _ = warnings.Should().Contain("Most recent figures: missing property current LongTermDebt");
    }

    [Fact]
    public void CurrentLongTermDebt_PrefersIncludingLeases() {
        (CompanyReport report, _) = WithCurrentBalanceSheet(
            ("LongTermDebt", 100m), ("LongTermDebtAndCapitalLeaseObligation", 150m));
        _ = report.CurLongTermDebt.Should().Be(150m);
    }

    [Fact]
    public void CurrentLongTermDebt_FallsBackToLongTermDebt() {
        (CompanyReport report, List<string> warnings) = WithCurrentBalanceSheet(("LongTermDebt", 100m));
        _ = report.CurLongTermDebt.Should().Be(100m);
        _ = warnings.Should().NotContain("Most recent figures: missing property current LongTermDebt");
    }

    [Fact]
    public void BalanceSheetDebtChange_PrefersIncludingLeases() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("ChangesInCash", 100m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("LongTermDebt", 100m), ("LongTermDebtAndCapitalLeaseObligation", 120m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("LongTermDebt", 150m), ("LongTermDebtAndCapitalLeaseObligation", 200m)));

        _ = Process(report);

        // Net issuance 200 - 120 = 80 is taken off the gross cash flow.
        _ = report.AverageNetCashFlow.Should().Be(20m);
    }

    [Fact]
    public void BalanceSheetDebtChange_MissingDebt_Warns() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("ChangesInCash", 100m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("StockholdersEquity", 1m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("LongTermDebt", 150m)));

        List<string> warnings = Process(report);

        _ = report.AverageNetCashFlow.Should().Be(100m);
        _ = warnings.Should().Contain(
            $"Balance sheet(report date: {Fy2021}) missing property 'LongTermDebtAndCapitalLeaseObligation' and 'LongTermDebt'");
        _ = warnings.Should().Contain($"Missing property 'NetIssuanceOfDebt'. Report date: {Fy2021}");
    }

    private static (decimal? NetCashFlow, List<string> Warnings) NetCashFlowOf(params (string Key, decimal Value)[] fields) {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(fields));
        List<string> warnings = Process(report);
        return (report.AverageNetCashFlow, warnings);
    }

    [Fact]
    public void GrossCashFlow_PrefersChangesInCash() {
        _ = NetCashFlowOf(("ChangesInCash", 50m), ("BeginningCashPosition", 100m), ("EndCashPosition", 300m))
            .NetCashFlow.Should().Be(50m);
    }

    [Fact]
    public void GrossCashFlow_FallsBackToEndLessBeginningCash() {
        _ = NetCashFlowOf(("BeginningCashPosition", 100m), ("EndCashPosition", 300m)).NetCashFlow.Should().Be(200m);
    }

    [Fact]
    public void GrossCashFlow_NeedsBothCashPositions() {
        (decimal? netCashFlow, List<string> warnings) = NetCashFlowOf(("EndCashPosition", 300m));
        _ = netCashFlow.Should().Be(0m);
        _ = warnings.Should().Contain($"Missing property 'GrossCashFlow'. Report date: {Fy2021}");
    }

    [Fact]
    public void DebtIssuance_PrefersNetIssuancePaymentsOfDebt() {
        _ = NetCashFlowOf(("ChangesInCash", 100m), ("NetIssuancePaymentsOfDebt", 30m), ("NetLongTermDebtIssuance", 10m))
            .NetCashFlow.Should().Be(70m);
    }

    [Fact]
    public void DebtIssuance_FallsBackToNetLongTermDebtIssuance() {
        _ = NetCashFlowOf(("ChangesInCash", 100m), ("NetLongTermDebtIssuance", 10m)).NetCashFlow.Should().Be(90m);
    }

    [Fact]
    public void NetIncome_FallsBackToIncomeStatement() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("ChangesInCash", 1m)));
        report.AddAnnualIncomeStatement(Fy2021, CreateRawReport(("NetIncomeContinuousOperations", 40m)));

        _ = Process(report);

        _ = report.AverageOwnerEarnings.Should().Be(40m);
    }

    [Fact]
    public void NetIncome_MissingFromIncomeStatement_Warns() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("ChangesInCash", 1m)));
        report.AddAnnualIncomeStatement(Fy2021, CreateRawReport(("TotalRevenue", 40m)));

        List<string> warnings = Process(report);

        _ = report.AverageOwnerEarnings.Should().BeNull("the year has neither net income nor operating cash flow (V8)");
        _ = warnings.Should().Contain($"Income statement(report date: {Fy2021}) missing property 'NetIncomeContinuousOperations'");
        _ = warnings.Should().Contain($"Missing property 'NetIncomeFromContinuingOperations'. Report date: {Fy2021}");
    }

    [Fact]
    public void WorkingCapital_MissingCurrentAssetsAndLiabilities_Warn() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("NetIncomeFromContinuingOperations", 100m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("CurrentLiabilities", 50m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("CurrentAssets", 130m)));

        List<string> warnings = Process(report);

        _ = report.AverageOwnerEarnings.Should().Be(100m);
        _ = warnings.Should().Contain($"Balance sheet(report date: {Fy2021}) missing property 'CurrentAssets'");
        _ = warnings.Should().Contain($"Balance sheet(report date: {Fy2021}) missing property 'CurrentLiabilities'");
        _ = warnings.Should().Contain($"Missing property 'ChangeInWorkingCapital'. Report date: {Fy2021}");
    }

    #endregion
}
