using System;
using System.Collections.Generic;
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
    private static CompanyFullDetailReport DetailOf(CompanyReport report) {
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
            roeCashFlowNumerator: f.RoeCashFlowNumerator, roeCashFlowEquity: f.RoeCashFlowEquity,
            roeOwnerEarningsNumerator: f.RoeOwnerEarningsNumerator, roeOwnerEarningsEquity: f.RoeOwnerEarningsEquity);
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
}
