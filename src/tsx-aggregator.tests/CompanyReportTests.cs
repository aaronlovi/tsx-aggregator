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

    #region B1 + B2 + B10 - owner earnings

    /// <summary>
    /// Owner earnings of a single annual cash-flow statement with net income 100 plus the given fields.
    /// Sign conventions as in the raw data: NetPPEPurchaseAndSale and PurchaseOfPPE are negative outflows,
    /// SaleOfPPE and CapitalExpenditureReported are positive, a negative ChangeInWorkingCapital means
    /// working capital increased.
    /// </summary>
    private static decimal OwnerEarningsOf(params (string Key, decimal Value)[] fields) {
        var cashFlow = CreateRawReport(fields);
        cashFlow["NetIncomeFromContinuingOperations"] = 100m;
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, cashFlow);
        return Process(report).AverageOwnerEarnings;
    }

    [Fact]
    public void OwnerEarnings_AddsBackDepreciation_SubtractsCapEx_AddsWorkingCapitalChange() {
        // B1: 100 + 20 - 30 - 5, not plain net income.
        _ = OwnerEarningsOf(
            ("Depreciation", 20m),
            ("NetPPEPurchaseAndSale", -30m),
            ("ChangeInWorkingCapital", -5m))
            .Should().Be(85m);
    }

    [Fact]
    public void OwnerEarnings_AllComponents() {
        // 100 + 20 (D&A) + 3 (depletion) + 4 (deferred tax) + 6 (other non-cash) - 30 (capex) - 5 (WC increase)
        _ = OwnerEarningsOf(
            ("DepreciationAndAmortization", 20m),
            ("Depletion", 3m),
            ("DeferredTax", 4m),
            ("OtherNonCashItems", 6m),
            ("NetPPEPurchaseAndSale", -30m),
            ("ChangeInWorkingCapital", -5m))
            .Should().Be(98m);
    }

    [Fact]
    public void OwnerEarnings_NoNetIncome_StillSumsTheOtherTerms() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("Depreciation", 20m),
            ("NetPPEPurchaseAndSale", -30m)));

        _ = Process(report).AverageOwnerEarnings.Should().Be(-10m);
    }

    // B2: capex is total net capex, -NetPPEPurchaseAndSale, with fallbacks
    // PurchaseOfPPE + SaleOfPPE -> PurchaseOfPPE -> -CapitalExpenditureReported (decision 1).

    [Fact]
    public void OwnerEarnings_CapEx_PrefersNetPPEPurchaseAndSale_OverCapitalExpenditureReported() {
        _ = OwnerEarningsOf(
            ("NetPPEPurchaseAndSale", -30m),
            ("PurchaseOfPPE", -35m),
            ("SaleOfPPE", 5m),
            ("CapitalExpenditureReported", 99m))
            .Should().Be(70m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_NetSaleOfPPE_IncreasesOwnerEarnings() {
        _ = OwnerEarningsOf(("NetPPEPurchaseAndSale", 15m)).Should().Be(115m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_FallsBackToPurchasePlusSale() {
        _ = OwnerEarningsOf(
            ("PurchaseOfPPE", -40m),
            ("SaleOfPPE", 10m),
            ("CapitalExpenditureReported", 99m))
            .Should().Be(70m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_FallsBackToPurchaseAlone() {
        _ = OwnerEarningsOf(
            ("PurchaseOfPPE", -40m),
            ("CapitalExpenditureReported", 99m))
            .Should().Be(60m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_FallsBackToCapitalExpenditureReported() {
        _ = OwnerEarningsOf(("CapitalExpenditureReported", 25m)).Should().Be(75m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_DepreciationIsNotPartOfCapEx() {
        // The old formula was CapEx = NetPPE + Depreciation, which cancelled the depreciation add-back.
        _ = OwnerEarningsOf(
            ("Depreciation", 20m),
            ("NetPPEPurchaseAndSale", -30m))
            .Should().Be(90m);
    }

    // B10: prefer DepreciationAndAmortization, else Depreciation + Amortization,
    // else DepreciationAmortizationDepletion (without adding Depletion again).
    // Each case has capex 10, since a year with D&A but no capex key is left out of the average (B12).

    private static decimal OwnerEarningsWithCapEx10(params (string Key, decimal Value)[] fields) =>
        OwnerEarningsOf([.. fields, ("NetPPEPurchaseAndSale", -10m)]);

    [Fact]
    public void OwnerEarnings_DepreciationAndAmortizationWithAmortization_CountsAmortizationOnce() {
        // The common B10 case: D&A and Amortization both present (often equal), no Depreciation key.
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("Amortization", 50m))
            .Should().Be(140m);
    }

    [Fact]
    public void OwnerEarnings_DepreciationAndAmortization_PreferredOverDepreciation() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("Depreciation", 30m),
            ("Amortization", 20m))
            .Should().Be(140m);
    }

    [Fact]
    public void OwnerEarnings_NoDepreciationAndAmortization_UsesDepreciationPlusAmortization() {
        _ = OwnerEarningsWithCapEx10(
            ("Depreciation", 30m),
            ("Amortization", 10m))
            .Should().Be(130m);
    }

    [Fact]
    public void OwnerEarnings_DepreciationAndAmortization_StillAddsDepletion() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("Depletion", 5m))
            .Should().Be(145m);
    }

    [Fact]
    public void OwnerEarnings_OnlyDepreciationAmortizationDepletion_UsesIt() {
        _ = OwnerEarningsWithCapEx10(("DepreciationAmortizationDepletion", 40m)).Should().Be(130m);
    }

    [Fact]
    public void OwnerEarnings_DepreciationAmortizationDepletionFallback_DoesNotAddDepletionAgain() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAmortizationDepletion", 40m),
            ("Depletion", 40m))
            .Should().Be(130m);
    }

    [Fact]
    public void OwnerEarnings_DepreciationAmortizationDepletion_IgnoredWhenDepreciationAndAmortizationPresent() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("DepreciationAmortizationDepletion", 70m))
            .Should().Be(140m);
    }

    #endregion

    #region B12 - missing or incomplete capex

    // Decision 14: when NetPPEPurchaseAndSale and PurchaseOfPPE are both present, use the larger outflow.

    [Fact]
    public void OwnerEarnings_CapEx_NetPPEMissesPurchases_UsesPurchasePlusSale() {
        // The SU 2024 shape: NetPPE +5 (just some proceeds) while purchases were -60.
        _ = OwnerEarningsOf(
            ("NetPPEPurchaseAndSale", 5m),
            ("PurchaseOfPPE", -60m),
            ("SaleOfPPE", 4m))
            .Should().Be(44m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_NetPPESmallerOutflow_NoSaleKey_UsesPurchase() {
        _ = OwnerEarningsOf(
            ("NetPPEPurchaseAndSale", -30m),
            ("PurchaseOfPPE", -50m))
            .Should().Be(50m);
    }

    [Fact]
    public void OwnerEarnings_CapEx_NetPPELargerOutflow_KeepsNetPPE() {
        // NetPPE = PurchaseOfPPE, ignoring the sale proceeds: the larger outflow is kept.
        _ = OwnerEarningsOf(
            ("NetPPEPurchaseAndSale", -40m),
            ("PurchaseOfPPE", -40m),
            ("SaleOfPPE", 10m))
            .Should().Be(60m);
    }

    // Decision 15: a year with D&A > 0 but no capex key is left out of the owner-earnings average.

    [Fact]
    public void OwnerEarnings_YearWithDepreciationButNoCapEx_LeftOutOfOwnerEarningsAverageOnly() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2020, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 10m),
            ("DepreciationAndAmortization", 20m),
            ("NetPPEPurchaseAndSale", -30m)));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangesInCash", 30m),
            ("DepreciationAndAmortization", 50m)));

        _ = Process(report);

        _ = report.AverageOwnerEarnings.Should().Be(90m);
        _ = report.AverageNetCashFlow.Should().Be(20m);
        _ = report.NumAnnualProcessedCashFlowReports.Should().Be(2);
    }

    [Fact]
    public void OwnerEarnings_YearWithNeitherDepreciationNorCapEx_IsKept() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2020, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("NetPPEPurchaseAndSale", -30m)));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 80m)));

        _ = Process(report).AverageOwnerEarnings.Should().Be(75m);
    }

    [Fact]
    public void OwnerEarnings_YearWithDepletionButNoCapEx_IsLeftOut() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2020, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("NetPPEPurchaseAndSale", -30m)));
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("Depletion", 10m)));

        _ = Process(report).AverageOwnerEarnings.Should().Be(70m);
    }

    [Fact]
    public void OwnerEarnings_NoYearHasCapEx_IsZero() {
        // Interim until W1, which makes this NotAvailable.
        _ = OwnerEarningsOf(("DepreciationAndAmortization", 50m)).Should().Be(0m);
    }

    // Decision 16: Depletion is not added again when DepreciationAmortizationDepletion equals D&A.

    [Fact]
    public void OwnerEarnings_DepletionAlreadyInsideDepreciationAndAmortization_NotAddedAgain() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("DepreciationAmortizationDepletion", 50m),
            ("Depletion", 5m))
            .Should().Be(140m);
    }

    [Fact]
    public void OwnerEarnings_DepletionSeparateFromDepreciationAndAmortization_IsAdded() {
        _ = OwnerEarningsWithCapEx10(
            ("DepreciationAndAmortization", 50m),
            ("DepreciationAmortizationDepletion", 55m),
            ("Depletion", 5m))
            .Should().Be(145m);
    }

    #endregion

    #region B4 - working capital fallback

    /// <summary>
    /// Owner earnings (net income 100, no other add-backs or capex) of a FY2021 cash-flow statement without
    /// ChangeInWorkingCapital, so the change comes from the FY2020 and FY2021 annual balance sheets.
    /// </summary>
    private static decimal OwnerEarningsWithBalanceSheets(RawReportDataMap prevBalanceSheet, RawReportDataMap curBalanceSheet) {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(("NetIncomeFromContinuingOperations", 100m)));
        report.AddAnnualBalanceSheet(Fy2020, prevBalanceSheet);
        report.AddAnnualBalanceSheet(Fy2021, curBalanceSheet);
        return Process(report).AverageOwnerEarnings;
    }

    [Fact]
    public void OwnerEarnings_ReportedChangeInWorkingCapital_UsedAsIs() {
        var report = NewReport();
        report.AddAnnualCashFlowStatement(Fy2021, CreateRawReport(
            ("NetIncomeFromContinuingOperations", 100m),
            ("ChangeInWorkingCapital", -30m)));
        report.AddAnnualBalanceSheet(Fy2020, CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m)));
        report.AddAnnualBalanceSheet(Fy2021, CreateRawReport(("CurrentAssets", 200m), ("CurrentLiabilities", 50m)));

        _ = Process(report).AverageOwnerEarnings.Should().Be(70m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalIncrease_FromBalanceSheets_ReducesOwnerEarnings() {
        // Working capital 50 -> 80: an increase of 30 consumes cash, so it is -30 in the cash-flow convention.
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 130m), ("CurrentLiabilities", 50m)))
            .Should().Be(70m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalDecrease_FromBalanceSheets_IncreasesOwnerEarnings() {
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 70m)))
            .Should().Be(120m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_ExcludesCash() {
        // Only cash grew (40 -> 100); non-cash working capital is 10 in both years.
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CashCashEquivalentsAndShortTermInvestments", 40m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 160m), ("CashCashEquivalentsAndShortTermInvestments", 100m), ("CurrentLiabilities", 50m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_CashFallsBackToCashAndCashEquivalents() {
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CashAndCashEquivalents", 40m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 160m), ("CashAndCashEquivalents", 100m), ("CurrentLiabilities", 50m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_PrefersCashIncludingShortTermInvestments() {
        // Using CashAndCashEquivalents (5 -> 5) would show a 60 increase in working capital.
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CashCashEquivalentsAndShortTermInvestments", 40m),
                ("CashAndCashEquivalents", 5m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 160m), ("CashCashEquivalentsAndShortTermInvestments", 100m),
                ("CashAndCashEquivalents", 5m), ("CurrentLiabilities", 50m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_ExcludesCurrentDebt() {
        // Only current debt grew (10 -> 40); non-debt current liabilities are 40 in both years.
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m), ("CurrentDebtAndCapitalLeaseObligation", 10m)),
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 80m), ("CurrentDebtAndCapitalLeaseObligation", 40m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_CurrentDebtFallsBackToCurrentDebt() {
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m), ("CurrentDebt", 10m)),
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 80m), ("CurrentDebt", 40m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_PrefersCurrentDebtIncludingLeases() {
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m),
                ("CurrentDebtAndCapitalLeaseObligation", 10m), ("CurrentDebt", 1m)),
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 80m),
                ("CurrentDebtAndCapitalLeaseObligation", 40m), ("CurrentDebt", 1m)))
            .Should().Be(100m);
    }

    [Fact]
    public void OwnerEarnings_WorkingCapitalFallback_MissingCurrentLiabilities_NoAdjustment() {
        _ = OwnerEarningsWithBalanceSheets(
            CreateRawReport(("CurrentAssets", 100m), ("CurrentLiabilities", 50m)),
            CreateRawReport(("CurrentAssets", 130m)))
            .Should().Be(100m);
    }

    #endregion
}
