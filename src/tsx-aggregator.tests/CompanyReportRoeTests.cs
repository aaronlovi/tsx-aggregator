using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using tsx_aggregator.models;

using static tsx_aggregator.tests.TestDataFactory;

namespace tsx_aggregator.tests;

/// <summary>
/// B11: return on equity over matched annual years with average equity per year (decisions 8 and 24), through
/// <see cref="CompanyReport"/>'s public surface and, end to end, the processed JSON and
/// <see cref="CompanyFullDetailReport"/>. Real sign conventions: CashDividendsPaid positive, NetPPEPurchaseAndSale
/// negative for an outflow.
/// </summary>
public class CompanyReportRoeTests {
    private static readonly DateOnly Fy2019 = new(2019, 12, 31);
    private static readonly DateOnly Fy2020 = new(2020, 12, 31);
    private static readonly DateOnly Fy2021 = new(2021, 12, 31);
    private static readonly DateOnly Fy2022 = new(2022, 12, 31);
    private static readonly DateOnly Q2Fy2022 = new(2022, 6, 30);

    private static CompanyReport NewReport() => new("TST", "Test Co", "TSX", 1000);

    private static CompanyReport Process(CompanyReport report) {
        report.ProcessReports(new List<string>());
        return report;
    }

    /// <summary>
    /// An annual year: cash flow with net cash flow <paramref name="netCashFlow"/> (no debt or stock issuance),
    /// dividends, owner earnings = net income - 10 of capex, and a matching income statement. A null equity means no
    /// balance sheet for the year; <paramref name="balanceSheetWithoutEquity"/> adds one without StockholdersEquity.
    /// </summary>
    private static void AddYear(
        CompanyReport report, DateOnly date, decimal? equity, decimal netCashFlow = 100m, decimal dividends = 20m,
        decimal netIncome = 110m, bool balanceSheetWithoutEquity = false, bool hasCapEx = true) {
        RawReportDataMap cashFlow = CreateRawReport(
            ("NetIncomeFromContinuingOperations", netIncome),
            ("ChangesInCash", netCashFlow),
            ("NetIssuancePaymentsOfDebt", 0m),
            ("NetCommonStockIssuance", 0m),
            ("NetPreferredStockIssuance", 0m),
            ("ChangeInWorkingCapital", 0m),
            ("CashDividendsPaid", dividends));
        if (hasCapEx)
            cashFlow["NetPPEPurchaseAndSale"] = -10m;
        else
            cashFlow["DepreciationAndAmortization"] = 50m; // D&A without capex: not an owner-earnings year (decision 15)
        report.AddAnnualCashFlowStatement(date, cashFlow);
        report.AddAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", netIncome)));
        AddBalanceSheet(report, date, equity, balanceSheetWithoutEquity);
    }

    private static void AddBalanceSheet(CompanyReport report, DateOnly date, decimal? equity, bool withoutEquity = false) {
        if (equity is decimal e)
            report.AddAnnualBalanceSheet(date, CreateRawReport(("StockholdersEquity", e)));
        else if (withoutEquity)
            report.AddAnnualBalanceSheet(date, CreateRawReport(("CurrentAssets", 1m)));
    }

    /// <summary>Equity at the latest quarter, which ROE must never use.</summary>
    private static void AddQuarter(CompanyReport report, DateOnly date, decimal equity) {
        report.AddNonAnnualCashFlowStatement(date, CreateRawReport(("ChangesInCash", 1m)));
        report.AddNonAnnualIncomeStatement(date, CreateRawReport(("NetIncomeContinuousOperations", 1m)));
        report.AddNonAnnualBalanceSheet(date, CreateRawReport(("StockholdersEquity", equity)));
    }

    /// <summary>The processed report as the API reads it back: serialized, parsed, and turned into the detail report.</summary>
    private static CompanyFullDetailReport DetailOf(
        CompanyReport report,
        int roeConsistencyMinYears = ScoringOptions.DefaultRoeConsistencyMinYears,
        int roeConsistencyWindowYears = ScoringOptions.DefaultRoeConsistencyWindowYears,
        int roeConsistencyMinUsableYears = ScoringOptions.DefaultRoeConsistencyMinUsableYears) {
        bool ok = ProcessedReportFields.TryParse(JsonSerializer.Serialize(report), out ProcessedReportFields? f);
        _ = ok.Should().BeTrue();
        return new CompanyFullDetailReport(
            exchange: "TSX", companySymbol: "TST", instrumentSymbol: "TST", companyName: "Test Co",
            instrumentName: "Test Co Common", pricePerShare: 10M, curLongTermDebt: f!.CurLongTermDebt,
            curTotalShareholdersEquity: f.CurTotalShareholdersEquity, curBookValue: f.CurBookValue,
            curNumShares: f.CurNumShares, averageNetCashFlow: f.AverageNetCashFlow,
            averageOwnerEarnings: f.AverageOwnerEarnings, curDividendsPaid: f.CurDividendsPaid,
            curAdjustedRetainedEarnings: f.CurAdjustedRetainedEarnings, oldestRetainedEarnings: f.OldestRetainedEarnings,
            numAnnualProcessedCashFlowReports: report.NumAnnualProcessedCashFlowReports,
            roe: RoeInputs.From(f),
            scoring: new ScoringOptions {
                RoeConsistencyMinYears = roeConsistencyMinYears, RoeConsistencyWindowYears = roeConsistencyWindowYears,
                RoeConsistencyMinUsableYears = roeConsistencyMinUsableYears,
            });
    }

    [Fact]
    public void Roe_MatchedYears_DividesSummedFlowsBySummedAverageEquity() {
        var report = NewReport();
        AddBalanceSheet(report, Fy2019, 800m);
        AddYear(report, Fy2020, 1000m, netCashFlow: 100m, dividends: 20m, netIncome: 110m);
        AddYear(report, Fy2021, 1200m, netCashFlow: 150m, dividends: 30m, netIncome: 160m);
        AddQuarter(report, Q2Fy2022, 5000m);

        _ = Process(report);

        // CF: (100 + 20) + (150 + 30) = 300. OE: (110 - 10) + (160 - 10) = 250.
        // Equity: (800 + 1000) / 2 + (1000 + 1200) / 2 = 900 + 1100 = 2000. The quarter's 5000 is not used.
        _ = report.RoeCashFlowNumerator.Should().Be(300m);
        _ = report.RoeCashFlowEquity.Should().Be(2000m);
        _ = report.RoeOwnerEarningsNumerator.Should().Be(250m);
        _ = report.RoeOwnerEarningsEquity.Should().Be(2000m);
    }

    [Fact]
    public void Roe_EndToEnd_DetailReportServesTheMatchedYearsRatio() {
        var report = NewReport();
        AddBalanceSheet(report, Fy2019, 800m);
        AddYear(report, Fy2020, 1000m, netCashFlow: 100m, dividends: 20m, netIncome: 110m);
        AddYear(report, Fy2021, 1200m, netCashFlow: 150m, dividends: 30m, netIncome: 160m);
        AddQuarter(report, Q2Fy2022, 5000m);

        CompanyFullDetailReport detail = DetailOf(Process(report));

        // 300 / 2000 = 15%, 250 / 2000 = 12.5%. The old formula divided the averages (125 each) by the
        // latest quarter's 5000: 2.5%, both failing.
        _ = detail.ReturnOnEquity_FromCashFlow.Should().Be(0.15M);
        _ = detail.ReturnOnEquity_FromOwnerEarnings.Should().Be(0.125M);
        _ = detail.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Pass);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Pass);
    }

    [Fact]
    public void Roe_OldestYearWithoutOpeningBalanceSheet_UsesClosingEquityAlone() {
        var report = NewReport();
        AddYear(report, Fy2020, 1000m);
        AddYear(report, Fy2021, 1200m);

        _ = Process(report);

        // 2020 has no opening balance sheet: 1000. 2021: (1000 + 1200) / 2 = 1100.
        _ = report.RoeCashFlowEquity.Should().Be(2100m);
        _ = report.RoeCashFlowNumerator.Should().Be(240m);
    }

    [Fact]
    public void Roe_OpeningBalanceSheetNotAboutAYearEarlier_UsesClosingEquityAlone() {
        var report = NewReport();
        var stubYearEnd = new DateOnly(2021, 6, 30); // fiscal year-end change: 181 days after the previous one
        AddYear(report, Fy2020, 1000m);
        AddYear(report, stubYearEnd, 1100m);
        var afterGap = new DateOnly(2023, 6, 30); // 730 days later: the years in between are missing
        AddYear(report, afterGap, 1300m);

        _ = Process(report);

        // 2020 has no opening; 2020 -> 2021-06 (181 days) and 2021-06 -> 2023-06 (730 days) are not about a year,
        // so every year uses its closing equity alone.
        _ = report.RoeCashFlowEquity.Should().Be(1000m + 1100m + 1300m);
    }

    [Fact]
    public void Roe_OpeningBalanceSheetWithoutEquity_UsesClosingEquityAlone() {
        var report = NewReport();
        AddYear(report, Fy2020, 1000m);
        AddYear(report, Fy2021, null, balanceSheetWithoutEquity: true);
        AddYear(report, Fy2022, 1200m);

        _ = Process(report);

        // 2021 has no closing equity: skipped in both sums. 2022's opening (2021) has none: closing alone.
        _ = report.RoeCashFlowEquity.Should().Be(1000m + 1200m);
        _ = report.RoeCashFlowNumerator.Should().Be(240m);
    }

    [Fact]
    public void Roe_YearWithoutClosingBalanceSheet_IsSkipped() {
        var report = NewReport();
        AddBalanceSheet(report, Fy2019, 800m);
        AddYear(report, Fy2020, 1000m, netCashFlow: 100m);
        AddYear(report, Fy2021, null, netCashFlow: 500m);
        AddYear(report, Fy2022, 1200m, netCashFlow: 150m, dividends: 30m);

        _ = Process(report);

        // 2021 is skipped; 2022's previous annual balance sheet (2020) is two years earlier: closing alone.
        _ = report.RoeCashFlowNumerator.Should().Be(120m + 180m);
        _ = report.RoeCashFlowEquity.Should().Be(900m + 1200m);
    }

    [Fact]
    public void Roe_FewerThanTwoUsableYears_FallsBackToAverageOverLatestAnnualEquity() {
        var report = NewReport();
        AddYear(report, Fy2020, null, netCashFlow: 100m, dividends: 20m, netIncome: 110m);
        AddYear(report, Fy2021, 1000m, netCashFlow: 200m, dividends: 40m, netIncome: 210m);
        AddQuarter(report, Q2Fy2022, 5000m);

        _ = Process(report);

        // One usable year (2021). Fallback (decision 8): averages over all years / latest annual equity, never the
        // quarter. CF: ((100 + 20) + (200 + 40)) / 2 = 180. OE: (100 + 200) / 2 = 150.
        _ = report.RoeCashFlowNumerator.Should().Be(180m);
        _ = report.RoeCashFlowEquity.Should().Be(1000m);
        _ = report.RoeOwnerEarningsNumerator.Should().Be(150m);
        _ = report.RoeOwnerEarningsEquity.Should().Be(1000m);
    }

    [Fact]
    public void Roe_NoAnnualEquity_IsNotAvailable() {
        var report = NewReport();
        AddYear(report, Fy2020, null, balanceSheetWithoutEquity: true);
        AddYear(report, Fy2021, null, balanceSheetWithoutEquity: true);
        AddQuarter(report, Q2Fy2022, 5000m);

        CompanyFullDetailReport detail = DetailOf(Process(report));

        // The quarter has equity, but ROE uses annual balance sheets only.
        _ = report.RoeCashFlowEquity.Should().BeNull();
        _ = report.RoeOwnerEarningsEquity.Should().BeNull();
        _ = detail.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.NotAvailable);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.NotAvailable);
    }

    [Fact]
    public void Roe_OwnerEarnings_UsesOnlyYearsThatCountForOwnerEarnings() {
        var report = NewReport();
        AddBalanceSheet(report, Fy2019, 800m);
        AddYear(report, Fy2020, 1000m, netIncome: 110m);
        AddYear(report, Fy2021, 1200m, netIncome: 160m, hasCapEx: false);
        AddYear(report, Fy2022, 1400m, netIncome: 210m);

        _ = Process(report);

        // 2021 has D&A but no capex: out of ROE (OE), still in ROE (CF).
        _ = report.RoeOwnerEarningsNumerator.Should().Be(100m + 200m);
        _ = report.RoeOwnerEarningsEquity.Should().Be(900m + 1300m);
        _ = report.RoeCashFlowEquity.Should().Be(900m + 1100m + 1300m);
    }

    [Fact]
    public void Roe_OwnerEarnings_OneUsableYear_FallsBackToAverageOwnerEarnings() {
        var report = NewReport();
        AddYear(report, Fy2020, 1000m, netIncome: 110m);
        AddYear(report, Fy2021, 1200m, netIncome: 160m, hasCapEx: false);

        _ = Process(report);

        // ROE (OE) has one year (2020): average owner earnings 100 / latest annual equity 1200.
        _ = report.RoeOwnerEarningsNumerator.Should().Be(100m);
        _ = report.RoeOwnerEarningsEquity.Should().Be(1200m);
        _ = report.RoeCashFlowEquity.Should().Be(1000m + 1100m);
    }

    [Fact]
    public void Roe_SummedEquityNotPositive_FailsEvenWithAPositiveRatio() {
        var report = NewReport();
        AddYear(report, Fy2020, -1000m, netCashFlow: -100m, dividends: 0m, netIncome: -90m);
        AddYear(report, Fy2021, -1200m, netCashFlow: -150m, dividends: 0m, netIncome: -140m);

        CompanyFullDetailReport detail = DetailOf(Process(report));

        // Negative / negative is a positive ratio, but Σ equity ≤ 0 fails (B7 rule, decision 8).
        _ = detail.ReturnOnEquity_FromCashFlow.Should().BePositive();
        _ = detail.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
    }

    #region C16 - ROE (OE) consistency, decision 28

    /// <summary>
    /// Annual years ending 2016..(2016 + count - 1), equity 1000 throughout (so average equity 1000 from the second
    /// year), owner earnings = roe x 1000 + 10 capex in each year (net income roe x 1000 + 10, capex 10).
    /// </summary>
    private static CompanyReport ReportWithYearlyRoe(params decimal[] roes) {
        var report = NewReport();
        AddBalanceSheet(report, new DateOnly(2015, 12, 31), 1000m);
        for (int i = 0; i < roes.Length; i++)
            AddYear(report, new DateOnly(2016 + i, 12, 31), 1000m, netIncome: roes[i] * 1000m + 10m);
        return Process(report);
    }

    [Fact]
    public void RoeByYear_IsOwnerEarningsOverAverageEquity_MostRecentFirst() {
        var report = NewReport();
        AddBalanceSheet(report, Fy2019, 800m);
        AddYear(report, Fy2020, 1000m, netIncome: 110m);
        AddYear(report, Fy2021, 1200m, netIncome: 160m, hasCapEx: false); // not an owner-earnings year
        AddYear(report, Fy2022, 1400m, netIncome: 270m);

        _ = Process(report);

        // 2022: 260 / 1300 = 0.2. 2020: 100 / 900. 2021 is left out (D&A, no capex).
        _ = report.RoeOwnerEarningsByYear.Should().Equal(
            new AnnualReturnOnEquity(Fy2022, 0.2m),
            new AnnualReturnOnEquity(Fy2020, 100m / 900m));
    }

    [Fact]
    public void RoeByYear_NonPositiveAverageEquity_HasNoRoe() {
        var report = NewReport();
        AddYear(report, Fy2020, -500m, netIncome: 110m);

        _ = Process(report);

        _ = report.RoeOwnerEarningsByYear.Should().Equal(new AnnualReturnOnEquity(Fy2020, null));
    }

    [Fact]
    public void RoeConsistency_FourOfLastFiveOverTenPercent_Passes() {
        // Oldest first; the last five are 12%, 5%, 15%, 11%, 20%: four over 10%. The oldest (1%) is outside the window.
        CompanyFullDetailReport detail = DetailOf(ReportWithYearlyRoe(0.01m, 0.12m, 0.05m, 0.15m, 0.11m, 0.20m));

        _ = detail.RoeOwnerEarningsYearsAbove10.Should().Be(4);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.Pass);
        _ = detail.AllChecks.Should().HaveCount(16);
    }

    [Fact]
    public void RoeConsistency_ThreeOfLastFive_Fails() {
        // Exactly 10% does not count (same "> 10%" as checks 14 and 15).
        CompanyFullDetailReport detail = DetailOf(ReportWithYearlyRoe(0.12m, 0.05m, 0.15m, 0.10m, 0.20m));

        _ = detail.RoeOwnerEarningsYearsAbove10.Should().Be(3);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.Fail);
    }

    [Theory]
    // Fewer usable years than the window: one miss allowed, like 4 of 5 (decision 28, refined).
    [InlineData(new[] { 0.20, 0.05, 0.20, 0.20 }, 3, 4, CheckResult.Pass)]   // 3 of 4
    [InlineData(new[] { 0.20, 0.05, 0.05, 0.20 }, 2, 4, CheckResult.Fail)]   // 2 of 4
    [InlineData(new[] { 0.20, 0.05, 0.20 }, 2, 3, CheckResult.Pass)]         // 2 of 3
    [InlineData(new[] { 0.05, 0.05, 0.20 }, 1, 3, CheckResult.Fail)]         // 1 of 3
    public void RoeConsistency_FewerUsableYearsThanTheWindow_AllowsOneMiss(
        double[] roes, int expectedAbove, int expectedConsidered, CheckResult expected) {
        CompanyFullDetailReport detail = DetailOf(ReportWithYearlyRoe([.. roes.Select(r => (decimal)r)]));

        _ = detail.RoeOwnerEarningsYearsAbove10.Should().Be(expectedAbove);
        _ = detail.RoeOwnerEarningsYearsConsidered.Should().Be(expectedConsidered);
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(expected);
    }

    [Fact]
    public void RoeConsistency_FewerThanThreeUsableYears_IsNotAvailable() {
        CompanyFullDetailReport detail = DetailOf(ReportWithYearlyRoe(0.20m, 0.20m));

        _ = detail.RoeOwnerEarningsYearsAbove10.Should().BeNull();
        _ = detail.RoeOwnerEarningsYearsConsidered.Should().BeNull();
        _ = detail.DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.NotAvailable);
    }

    [Fact]
    public void RoeConsistency_UsesTheSettings() {
        CompanyReport report = ReportWithYearlyRoe(0.12m, 0.05m, 0.15m, 0.10m, 0.20m);

        _ = DetailOf(report, roeConsistencyMinYears: 3).DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.Pass);
        _ = DetailOf(report, roeConsistencyMinYears: 2, roeConsistencyWindowYears: 2, roeConsistencyMinUsableYears: 2)
            .DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.Fail); // 20%, 10%: one over
        CompanyReport twoYears = ReportWithYearlyRoe(0.20m, 0.20m);
        _ = DetailOf(twoYears, roeConsistencyMinUsableYears: 2)
            .DoesPassCheck_ROE_FromOwnerEarnings_Consistent.Should().Be(CheckResult.Pass); // 2 of 2, needs 1
    }

    #endregion
}
