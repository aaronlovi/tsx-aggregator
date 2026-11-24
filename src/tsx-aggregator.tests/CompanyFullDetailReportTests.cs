using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using tsx_aggregator.models;
using tsx_aggregator.Services;

namespace tsx_aggregator.tests;

/// <summary>
/// Ratios, checks and max price in <see cref="CompanyFullDetailReport"/>. Dividends paid are positive,
/// as in the raw data (CashDividendsPaid, Scoring Bug Fix Plan section 2.5).
/// </summary>
public class CompanyFullDetailReportTests {
    /// <summary>Market cap 1,000 (100 shares at 10); book value and equity 1,000.</summary>
    private static CompanyFullDetailReport Report(
        decimal averageNetCashFlow, decimal? averageOwnerEarnings, decimal curDividendsPaid, decimal? latestOwnerEarnings = null) => new(
        exchange: "TSX",
        companySymbol: "TST",
        instrumentSymbol: "TST",
        companyName: "Test Co",
        instrumentName: "Test Co Common",
        pricePerShare: 10M,
        curLongTermDebt: 0M,
        curShortTermDebt: 0M,
        curTotalShareholdersEquity: 1_000M,
        curBookValue: 1_000M,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings, latestOwnerEarnings: latestOwnerEarnings, latestNetCashFlow: null,
        curDividendsPaid: curDividendsPaid,
        averageDividendsPaid: curDividendsPaid,
        curAdjustedRetainedEarnings: 100M,
        oldestRetainedEarnings: 50M,
        numAnnualProcessedCashFlowReports: 5,
        roe: new RoeInputs(
            CashFlowNumerator: averageNetCashFlow,
            CashFlowEquity: 1_000M,
            OwnerEarningsNumerator: averageOwnerEarnings,
            OwnerEarningsEquity: 1_000M,
            OwnerEarningsByYear: TestDataFactory.ConsistentRoeYears()),
        scoring: new ScoringOptions());

    #region B5 - dividends in the est. return and max price

    [Fact]
    public void EstReturnFromCashFlow_AddsDividendsBack() {
        // Net cash flow is after dividends (they are a financing outflow), so they are added back like buybacks.
        _ = Report(averageNetCashFlow: 50M, averageOwnerEarnings: 60M, curDividendsPaid: 20M)
            .EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().Be(7M);
    }

    [Fact]
    public void EstReturnFromOwnerEarnings_IgnoresDividends() {
        // Owner earnings are pre-dividend: the whole amount is the shareholders' return.
        _ = Report(averageNetCashFlow: 50M, averageOwnerEarnings: 60M, curDividendsPaid: 20M)
            .EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(6M);
    }

    [Fact]
    public void EstReturns_NoDividends() {
        var report = Report(averageNetCashFlow: 50M, averageOwnerEarnings: 60M, curDividendsPaid: 0M);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().Be(5M);
        _ = report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(6M);
    }

    [Fact]
    public void MaxPrice_UsesTheSameNumeratorsAsTheReturns() {
        // min(3 x book 1,000 = 3,000; 20 x (50 + 20) = 1,400; 20 x 60 = 1,200) / 100 shares
        _ = Report(averageNetCashFlow: 50M, averageOwnerEarnings: 60M, curDividendsPaid: 20M)
            .MaxPrice.Should().Be(12M);
    }

    [Fact]
    public void DividendPayer_PassesCashFlowReturnCheck() {
        // 4% before dividends are added back, 6% after.
        var report = Report(averageNetCashFlow: 40M, averageOwnerEarnings: 60M, curDividendsPaid: 20M);

        _ = report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough.Should().Be(CheckResult.Pass);
    }

    #endregion

    #region B7 - equity or book value of 0 or less fails its checks

    /// <summary>
    /// Market cap 1,000, long-term debt 10, positive cash flow and owner earnings. With a negative equity, the ROE
    /// ratios are negative / negative, so they would pass without the sign rule.
    /// </summary>
    private static CompanyFullDetailReport ReportWithEquity(
        decimal equity, decimal bookValue, decimal averageNetCashFlow = 50M, decimal averageOwnerEarnings = 60M) => new(
        exchange: "TSX",
        companySymbol: "TST",
        instrumentSymbol: "TST",
        companyName: "Test Co",
        instrumentName: "Test Co Common",
        pricePerShare: 10M,
        curLongTermDebt: 10M,
        curShortTermDebt: 0M,
        curTotalShareholdersEquity: equity,
        curBookValue: bookValue,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings, latestOwnerEarnings: null, latestNetCashFlow: null,
        curDividendsPaid: 0M,
        averageDividendsPaid: 0M,
        curAdjustedRetainedEarnings: 100M,
        oldestRetainedEarnings: 50M,
        numAnnualProcessedCashFlowReports: 5,
        roe: new RoeInputs(
            CashFlowNumerator: averageNetCashFlow,
            CashFlowEquity: equity,
            OwnerEarningsNumerator: averageOwnerEarnings,
            OwnerEarningsEquity: equity,
            OwnerEarningsByYear: TestDataFactory.ConsistentRoeYears()),
        scoring: new ScoringOptions());

    [Fact]
    public void NegativeEquity_FailsDebtToEquityAndBothRoeChecks() {
        // Debt-to-equity is 10 / -100 = -0.1 (< 0.5); ROE is -50 / -100 = 0.5 (> 0.10).
        var report = ReportWithEquity(equity: -100M, bookValue: 500M, averageNetCashFlow: -50M, averageOwnerEarnings: -60M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void ZeroEquity_FailsBothRoeChecks() {
        // DivSafe returns decimal.MaxValue for a zero denominator, which is > 0.10.
        var report = ReportWithEquity(equity: 0M, bookValue: 500M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void NegativeBookValue_FailsPriceToBookAndDebtToBook() {
        // Goodwill larger than equity: price-to-book 1,000 / -200 = -5 (<= 3); debt-to-book -0.05 (< 1).
        var report = ReportWithEquity(equity: 300M, bookValue: -200M);

        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void NegativeBookValue_PositiveEquity_KeepsEquityChecks() {
        var report = ReportWithEquity(equity: 300M, bookValue: -200M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Pass);
    }

    [Fact]
    public void ZeroBookValue_FailsPriceToBookAndDebtToBook() {
        var report = ReportWithEquity(equity: 300M, bookValue: 0M);

        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void NegativeEquity_RatiosStayHonest() {
        // The sign rule is in the checks, not in DivSafe: the ratios still show the real quotient.
        var report = ReportWithEquity(equity: -100M, bookValue: -200M, averageNetCashFlow: -50M);

        _ = report.DebtToEquityRatio.Should().Be(-0.1M);
        _ = report.ReturnOnEquity_FromCashFlow.Should().Be(0.5M);
        _ = report.CurPriceToBookRatio.Should().Be(-5M);
    }

    [Fact]
    public void PositiveEquityAndBook_ChecksUnchanged() {
        var report = ReportWithEquity(equity: 300M, bookValue: 500M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Pass);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Pass);
    }

    #endregion

    #region W1 - missing data is NotAvailable

    /// <summary>
    /// Market cap 1,000, equity 900, book 800, debt 10, NCF 100, OE 120, dividends 20, retained earnings 300 (up
    /// from 100), 5 years: passes every check except 2 (book value 800 is not over 150M). Each figure can be null.
    /// </summary>
    private static CompanyFullDetailReport FullReport(
        decimal pricePerShare = 10M,
        decimal? equity = 900M,
        decimal? bookValue = 800M,
        decimal? averageNetCashFlow = 100M,
        decimal? averageOwnerEarnings = 120M,
        decimal? curDividendsPaid = 20M,
        decimal? curAdjustedRetainedEarnings = 300M,
        decimal? oldestRetainedEarnings = 100M) => new(
        exchange: "TSX",
        companySymbol: "TST",
        instrumentSymbol: "TST",
        companyName: "Test Co",
        instrumentName: "Test Co Common",
        pricePerShare: pricePerShare,
        curLongTermDebt: 10M,
        curShortTermDebt: 0M,
        curTotalShareholdersEquity: equity,
        curBookValue: bookValue,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings, latestOwnerEarnings: null, latestNetCashFlow: null,
        curDividendsPaid: curDividendsPaid,
        averageDividendsPaid: curDividendsPaid,
        curAdjustedRetainedEarnings: curAdjustedRetainedEarnings,
        oldestRetainedEarnings: oldestRetainedEarnings,
        numAnnualProcessedCashFlowReports: 5,
        roe: new RoeInputs(
            CashFlowNumerator: averageNetCashFlow,
            CashFlowEquity: equity,
            OwnerEarningsNumerator: averageOwnerEarnings,
            OwnerEarningsEquity: equity,
            OwnerEarningsByYear: TestDataFactory.ConsistentRoeYears()),
        scoring: new ScoringOptions());

    /// <summary>The 1-based numbers of the checks with the given result.</summary>
    private static int[] ChecksWith(CompanyFullDetailReport report, CheckResult result) =>
        [.. report.AllChecks.Select((check, i) => (check, number: i + 1)).Where(x => x.check == result).Select(x => x.number)];

    [Fact]
    public void ScaledReport_PassesEverything() {
        // The same company scaled by a million, so book value clears check 2's 150M.
        var report = FullReport(pricePerShare: 10_000_000M, equity: 900_000_000M, bookValue: 800_000_000M,
            averageNetCashFlow: 100_000_000M, averageOwnerEarnings: 120_000_000M, curDividendsPaid: 20_000_000M,
            curAdjustedRetainedEarnings: 300_000_000M, oldestRetainedEarnings: 100_000_000M);

        _ = ChecksWith(report, CheckResult.Fail).Should().BeEmpty();
        _ = report.OverallScore.Should().Be(16);
        _ = report.NotAvailableCount.Should().Be(0);
        _ = report.DoesPassCheck_Overall.Should().BeTrue();
    }

    [Fact]
    public void MissingEquity_EquityChecksAndBookValueChecksNotAvailable() {
        var report = FullReport(equity: null, bookValue: null);

        _ = ChecksWith(report, CheckResult.NotAvailable).Should().Equal(1, 2, 3, 10, 14, 15);
        _ = report.DebtToEquityRatio.Should().BeNull();
        _ = report.ReturnOnEquity_FromCashFlow.Should().BeNull();
        _ = report.CurPriceToBookRatio.Should().BeNull();
        _ = report.MaxPrice.Should().BeNull();
    }

    [Fact]
    public void MissingPrice_PriceChecksNotAvailable() {
        // Decision 20: no quote means no market cap, so price-to-book and the four return checks are n/a.
        var report = FullReport(pricePerShare: 0M);

        _ = report.CurMarketCap.Should().BeNull();
        _ = ChecksWith(report, CheckResult.NotAvailable).Should().Equal(3, 6, 7, 8, 9);
        _ = report.EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().BeNull();
        _ = report.PercentageUpside.Should().BeNull();
        _ = report.MaxPrice.Should().NotBeNull();
    }

    [Fact]
    public void MissingNetCashFlow_CashFlowChecksNotAvailable() {
        var report = FullReport(averageNetCashFlow: null);

        _ = ChecksWith(report, CheckResult.NotAvailable).Should().Equal(4, 6, 8, 14);
    }

    [Fact]
    public void MissingOwnerEarnings_OwnerEarningsChecksNotAvailable() {
        var report = FullReport(averageOwnerEarnings: null);

        _ = ChecksWith(report, CheckResult.NotAvailable).Should().Equal(5, 7, 9, 15);
    }

    [Fact]
    public void MissingRetainedEarnings_RetainedEarningsChecksNotAvailable() {
        _ = ChecksWith(FullReport(curAdjustedRetainedEarnings: null), CheckResult.NotAvailable).Should().Equal(11, 13);
        _ = ChecksWith(FullReport(oldestRetainedEarnings: null), CheckResult.NotAvailable).Should().Equal(13);
    }

    [Fact]
    public void MissingDividends_CashFlowReturnNotAvailable() {
        _ = ChecksWith(FullReport(curDividendsPaid: null), CheckResult.NotAvailable).Should().Equal(6, 8);
    }

    [Fact]
    public void ZeroEquity_FailsRatherThanNotAvailable() {
        // The B7 sign rule still applies when the figure is present but 0 or less.
        var report = FullReport(equity: 0M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.ReturnOnEquity_FromCashFlow.Should().BeNull();
    }

    [Fact]
    public void Score_CountsPassOnly_AndCountsNotAvailable() {
        var report = FullReport(averageOwnerEarnings: null);

        // Check 2 fails (book value 800 < 150M); checks 5, 7, 9 and 15 are n/a.
        _ = report.OverallScore.Should().Be(11);
        _ = report.NotAvailableCount.Should().Be(4);
        _ = report.DoesPassCheck_Overall.Should().BeFalse();
    }

    [Fact]
    public void Json_ChecksAreStrings_MissingNumbersAreNull() {
        string json = JsonSerializer.Serialize(FullReport(equity: null, bookValue: null), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        _ = root.GetProperty("doesPassCheck_DebtToEquitySmallEnough").GetString().Should().Be("NotAvailable");
        _ = root.GetProperty("doesPassCheck_IsHistoryLongEnough").GetString().Should().Be("Pass");
        _ = root.GetProperty("didAdjustedRetainedEarningsIncrease").GetString().Should().Be("Pass");
        _ = root.GetProperty("curTotalShareholdersEquity").ValueKind.Should().Be(JsonValueKind.Null);
        _ = root.GetProperty("maxPrice").ValueKind.Should().Be(JsonValueKind.Null);
        _ = root.GetProperty("notAvailableCount").GetInt32().Should().Be(6);
        _ = root.TryGetProperty("allChecks", out _).Should().BeFalse();
    }

    #endregion

    #region X3 - short-term debt in the debt checks

    private static CompanyFullDetailReport ReportWithDebt(decimal longTermDebt, decimal shortTermDebt) =>
        new CompanyFullDetailReport(
            exchange: "TSX", companySymbol: "TST", instrumentSymbol: "TST", companyName: "Test Co",
            instrumentName: "Test Co Common", pricePerShare: 10M,
            curLongTermDebt: longTermDebt,
            curShortTermDebt: shortTermDebt,
            curTotalShareholdersEquity: 1_000M, curBookValue: 800M, curNumShares: 100,
            averageNetCashFlow: 50M, averageOwnerEarnings: 60M, latestOwnerEarnings: null, latestNetCashFlow: null, curDividendsPaid: 0M, averageDividendsPaid: 0M, curAdjustedRetainedEarnings: 100M,
            oldestRetainedEarnings: 50M, numAnnualProcessedCashFlowReports: 5,
            roe: new RoeInputs(50M, 1_000M, 60M, 1_000M, TestDataFactory.ConsistentRoeYears()),
            scoring: new ScoringOptions());

    [Fact]
    public void TotalDebt_IsLongTermPlusShortTerm() {
        _ = ReportWithDebt(300M, 150M).CurTotalDebt.Should().Be(450M);
    }

    [Fact]
    public void DebtToEquity_CountsShortTermDebt() {
        // Long-term debt alone: 300 / 1,000 = 0.3 passes; with 250 due within a year, 550 / 1,000 = 0.55 fails.
        _ = ReportWithDebt(300M, 0M).DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Pass);
        CompanyFullDetailReport report = ReportWithDebt(300M, 250M);
        _ = report.DebtToEquityRatio.Should().Be(0.55M);
        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void DebtToBook_CountsShortTermDebt() {
        // Book value 800: 700 / 800 passes; 700 + 200 = 900 / 800 fails.
        _ = ReportWithDebt(700M, 0M).DoesPassCheck_DebtToBookRatioSmallEnough.Should().Be(CheckResult.Pass);
        CompanyFullDetailReport report = ReportWithDebt(700M, 200M);
        _ = report.LongTermDebtToBookRatio.Should().Be(1.125M);
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().Be(CheckResult.Fail);
    }

    [Fact]
    public void OnlyShortTermDebt_IsNotDebtFree() {
        _ = ReportWithDebt(0M, 600M).DoesPassCheck_DebtToEquitySmallEnough.Should().Be(CheckResult.Fail);
    }

    #endregion

    #region R94 - dividends averaged like the net cash flow

    private static CompanyFullDetailReport ReportWithDividends(decimal? current, decimal? average) =>
        new CompanyFullDetailReport(
            exchange: "TSX", companySymbol: "TST", instrumentSymbol: "TST", companyName: "Test Co",
            instrumentName: "Test Co Common", pricePerShare: 10M,
            curLongTermDebt: 0M,
            curShortTermDebt: 0M,
            curTotalShareholdersEquity: 1_000M, curBookValue: 1_000M, curNumShares: 100,
            averageNetCashFlow: 30M, averageOwnerEarnings: 100M, latestOwnerEarnings: null, latestNetCashFlow: null,
            curDividendsPaid: current,
            averageDividendsPaid: average,
            curAdjustedRetainedEarnings: 100M, oldestRetainedEarnings: 50M, numAnnualProcessedCashFlowReports: 5,
            roe: new RoeInputs(50M, 1_000M, 60M, 1_000M, TestDataFactory.ConsistentRoeYears()),
            scoring: new ScoringOptions());

    [Fact]
    public void CashFlowReturn_AddsBackTheAverageDividends() {
        // Market cap 1,000. A special dividend of 500 in the latest year, 120 a year on average:
        // (30 + 120) / 1,000 = 15%, not (30 + 500) / 1,000 = 53%.
        CompanyFullDetailReport report = ReportWithDividends(current: 500M, average: 120M);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().Be(15M);
        _ = report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig.Should().Be(CheckResult.Pass);
    }

    [Fact]
    public void CashFlowReturn_SuspendedDividend_StillAddsBackTheEarlierOnes() {
        // No dividend now, 40 a year on average: the cash generated was (30 + 40) / 1,000 = 7%.
        _ = ReportWithDividends(current: 0M, average: 40M).EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().Be(7M);
    }

    [Fact]
    public void CashFlowReturn_NoAverageDividends_IsNull() {
        _ = ReportWithDividends(current: 10M, average: null).EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().BeNull();
    }

    [Fact]
    public void MaxPrice_UsesTheAverageDividends() {
        // Limits: 3 x book = 3,000; owner earnings 20 x 100 = 2,000; cash flow 20 x (30 + average 20) = 1,000.
        // The lowest, 1,000, over 100 shares is 10. With the latest year's 500 it would be 20 x 530 = 10,600 and the
        // price 20 (the owner-earnings limit).
        _ = ReportWithDividends(current: 500M, average: 20M).MaxPrice.Should().Be(10M);
    }

    #endregion

    #region V2 - the lower of the average and the latest year's owner earnings

    // Market cap 1,000; net cash flow 50 + dividends 20.
    private static CompanyFullDetailReport ReportWithLatest(decimal? average, decimal? latest) =>
        Report(averageNetCashFlow: 50M, averageOwnerEarnings: average, curDividendsPaid: 20M, latestOwnerEarnings: latest);

    [Fact]
    public void OwnerEarningsReturn_LatestYearBelowTheAverage_UsesTheLatestYear() {
        CompanyFullDetailReport report = ReportWithLatest(average: 60M, latest: 30M);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(3M);
        _ = report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(60)]
    public void OwnerEarningsReturn_LatestYearNotBelowTheAverage_UsesTheAverage(int latest) {
        _ = ReportWithLatest(average: 60M, latest: latest)
            .EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(6M);
    }

    [Fact]
    public void OwnerEarningsReturn_NoUsableLatestYear_UsesTheAverage() {
        CompanyFullDetailReport report = ReportWithLatest(average: 60M, latest: null);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(6M);
        _ = report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough.Should().Be(CheckResult.Pass);
        _ = report.MaxPrice.Should().Be(12M);
    }

    [Fact]
    public void OwnerEarningsReturn_LatestYearWithoutAnAverage_IsNotAvailable() {
        CompanyFullDetailReport report = ReportWithLatest(average: null, latest: 30M);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().BeNull();
        _ = report.MaxPrice.Should().BeNull();
    }

    [Fact]
    public void AverageOwnerEarningsPositive_LowLatestYear_StaysOnTheAverage() {
        _ = ReportWithLatest(average: 60M, latest: -10M).DoesPassCheck_AvgOwnerEarningsPositive.Should().Be(CheckResult.Pass);
    }

    [Fact]
    public void OwnerEarningsReturnNotTooBig_LowLatestYear_StaysOnTheAverage() {
        // 45% on the average looks like wrong data; a weak latest year (10%) must not turn that into a pass.
        CompanyFullDetailReport report = ReportWithLatest(average: 450M, latest: 100M);

        _ = report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(10M);
        _ = report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough.Should().Be(CheckResult.Pass);
    }

    [Fact]
    public void OtherFigures_LowLatestYear_AreUnchanged() {
        CompanyFullDetailReport low = ReportWithLatest(average: 60M, latest: -10M);
        CompanyFullDetailReport none = ReportWithLatest(average: 60M, latest: null);

        _ = low.EstimatedNextYearTotalReturnPercentage_FromCashFlow.Should().Be(none.EstimatedNextYearTotalReturnPercentage_FromCashFlow);
        _ = low.EstimatedNextYearBookValue_FromOwnerEarnings.Should().Be(none.EstimatedNextYearBookValue_FromOwnerEarnings);
        _ = low.ReturnOnEquity_FromOwnerEarnings.Should().Be(none.ReturnOnEquity_FromOwnerEarnings);
        _ = low.OverallScore.Should().Be(none.OverallScore - 1, "only check 7 fails");
    }

    [Fact]
    public void MaxPrice_LatestYearBelowTheAverage_UsesTheLatestYear() {
        // min(3 x book 1,000 = 3,000; 20 x (50 + 20) = 1,400; 20 x 30 = 600) / 100 shares
        _ = ReportWithLatest(average: 60M, latest: 30M).MaxPrice.Should().Be(6M);
    }

    [Fact]
    public void FromReplyItem_ReadsLatestOwnerEarnings_UnsetIsNull() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100,
            AverageOwnerEarnings = 60,
        };
        _ = CompanyFullDetailReport.FromReplyItem(item).LatestOwnerEarnings.Should().BeNull();

        item.LatestOwnerEarnings = 0;
        _ = CompanyFullDetailReport.FromReplyItem(item).LatestOwnerEarnings.Should().Be(0M, "0 is a figure, not missing");

        item.LatestOwnerEarnings = 30;
        _ = CompanyFullDetailReport.FromReplyItem(item).EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings.Should().Be(3M);
    }

    [Fact]
    public void FromReplyItem_ReadsLatestNetCashFlow_UnsetIsNull() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100,
        };
        _ = CompanyFullDetailReport.FromReplyItem(item).LatestNetCashFlow.Should().BeNull();

        item.LatestNetCashFlow = 0;
        _ = CompanyFullDetailReport.FromReplyItem(item).LatestNetCashFlow.Should().Be(0M, "0 is a figure, not missing");
    }

    #endregion

    #region W1 - gRPC reply mapping

    [Fact]
    public void FromReplyItem_UnsetOptionalFields_AreNull() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100, CurrentLongTermDebt = 10,
            NumAnnualProcessedCashFlowReports = 5,
        };

        var report = CompanyFullDetailReport.FromReplyItem(item);

        _ = report.CurTotalShareholdersEquity.Should().BeNull();
        _ = report.CurBookValue.Should().BeNull();
        _ = report.AverageNetCashFlow.Should().BeNull();
        _ = report.AverageOwnerEarnings.Should().BeNull();
        _ = report.CurDividendsPaid.Should().BeNull();
        _ = report.CurAdjustedRetainedEarnings.Should().BeNull();
        _ = report.OldestRetainedEarnings.Should().BeNull();
        _ = report.CurLongTermDebt.Should().Be(10M);
        _ = report.CurShortTermDebt.Should().Be(0M, "an older server does not send it");
    }

    [Fact]
    public void FromReplyItem_UnsetPrice_IsZeroAndHasNoMarketCap() {
        // Threw a NullReferenceException in the DecimalValue conversion (found during step 6 of the code review).
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", CurrentNumShares = 100, NumAnnualProcessedCashFlowReports = 5,
        };

        var report = CompanyFullDetailReport.FromReplyItem(item);

        _ = report.PricePerShare.Should().Be(0M);
        _ = report.CurMarketCap.Should().BeNull("no quote means no market cap (decision 20)");
    }

    [Fact]
    public void FromReplyItem_ReadsAverageDividends() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100,
            CurrentDividendsPaid = 180, AverageDividendsPaid = 47, NumAnnualProcessedCashFlowReports = 5,
        };

        var report = CompanyFullDetailReport.FromReplyItem(item);

        _ = report.CurDividendsPaid.Should().Be(180M);
        _ = report.AverageDividendsPaid.Should().Be(47M);
    }

    [Fact]
    public void FromReplyItem_NoAverageDividends_UsesTheLatestYear() {
        // An older server doesn't send the average: score as before R94.
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100,
            CurrentDividendsPaid = 180, NumAnnualProcessedCashFlowReports = 5,
        };

        _ = CompanyFullDetailReport.FromReplyItem(item).AverageDividendsPaid.Should().Be(180M);
    }

    [Fact]
    public void FromReplyItem_ReadsShortTermDebt() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100, CurrentLongTermDebt = 10,
            CurrentShortTermDebt = 7, NumAnnualProcessedCashFlowReports = 5,
        };

        _ = CompanyFullDetailReport.FromReplyItem(item).CurShortTermDebt.Should().Be(7M);
    }

    [Fact]
    public void FromReplyItem_SetOptionalFields_IncludingZero_AreRead() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, CurrentNumShares = 100,
            CurrentTotalShareholdersEquity = 1_200, CurrentBookValue = 1_000, AverageNetCashFlow = 100,
            AverageOwnerEarnings = 120, CurrentDividendsPaid = 0, CurrentAdjustedRetainedEarnings = 300,
            OldestRetainedEarnings = 100, NumAnnualProcessedCashFlowReports = 5,
            LastUpdatedDate = Timestamp.FromDateTimeOffset(new System.DateTimeOffset(2026, 8, 26, 0, 0, 0, System.TimeSpan.Zero)),
        };

        var report = CompanyFullDetailReport.FromReplyItem(item);

        _ = report.CurTotalShareholdersEquity.Should().Be(1_200M);
        _ = report.CurDividendsPaid.Should().Be(0M);
        _ = report.OldestRetainedEarnings.Should().Be(100M);
        _ = report.LastUpdatedDate.Should().NotBeNull();
    }

    [Fact]
    public void RoeInputs_From_ReadsEachRoeField() {
        IReadOnlyList<AnnualReturnOnEquity> years = TestDataFactory.ConsistentRoeYears();
        var fields = new ProcessedReportFields(
            CurNumShares: 100, CurTotalShareholdersEquity: 1, CurGoodwill: 0, CurIntangibles: 0, CurLongTermDebt: 0,
            CurDividendsPaid: 0, CurAdjustedRetainedEarnings: 0, OldestRetainedEarnings: 0, CurBookValue: 1,
            AverageNetCashFlow: 0, AverageOwnerEarnings: 0, RoeCashFlowNumerator: 11, RoeCashFlowEquity: 22,
            RoeOwnerEarningsNumerator: 33, RoeOwnerEarningsEquity: 44, RoeOwnerEarningsByYear: years);

        _ = RoeInputs.From(fields).Should().Be(new RoeInputs(11M, 22M, 33M, 44M, years));
    }

    [Fact]
    public void FromReplyItem_ScoringSettings_AreRead_AndZeroFromAnOlderServerMeansTheDefaults() {
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, RoeConsistencyMinYears = 3, RoeConsistencyWindowYears = 6,
            RoeConsistencyMinUsableYears = 2,
        };

        var report = CompanyFullDetailReport.FromReplyItem(item);
        var olderServer = CompanyFullDetailReport.FromReplyItem(new GetStocksDataReplyItem { Exchange = "TSX", PerSharePrice = 10M });

        _ = report.RoeConsistencyMinYears.Should().Be(3);
        _ = report.RoeConsistencyWindowYears.Should().Be(6);
        _ = report.RoeConsistencyMinUsableYears.Should().Be(2);
        _ = olderServer.RoeConsistencyMinYears.Should().Be(ScoringOptions.DefaultRoeConsistencyMinYears);
        _ = olderServer.RoeConsistencyWindowYears.Should().Be(ScoringOptions.DefaultRoeConsistencyWindowYears);
        _ = olderServer.RoeConsistencyMinUsableYears.Should().Be(ScoringOptions.DefaultRoeConsistencyMinUsableYears);
    }

    [Fact]
    public void FromReplyItem_RoeInputsDatesAndEstimatedFlags_AreRead() {
        var annual = new System.DateTimeOffset(2025, 12, 31, 0, 0, 0, System.TimeSpan.Zero);
        var quarterly = new System.DateTimeOffset(2026, 6, 30, 0, 0, 0, System.TimeSpan.Zero);
        var item = new GetStocksDataReplyItem {
            Exchange = "TSX", CompanySymbol = "TST", InstrumentSymbol = "TST", CompanyName = "Test Co",
            InstrumentName = "Test Co Common", PerSharePrice = 10M, RoeCashFlowNumerator = 11, RoeCashFlowEquity = 22,
            RoeOwnerEarningsNumerator = 33, RoeOwnerEarningsEquity = 44,
            MostRecentAnnualReportDate = Timestamp.FromDateTimeOffset(annual),
            MostRecentQuarterlyReportDate = Timestamp.FromDateTimeOffset(quarterly),
            CurrentRetainedEarningsEstimated = true, OldestRetainedEarningsEstimated = true,
        };
        item.RoeOwnerEarningsByYear.Add(new AnnualRoe { YearEnd = "2025-12-31", Roe = 0.12M });
        item.RoeOwnerEarningsByYear.Add(new AnnualRoe { YearEnd = "2024-12-31" });

        var report = CompanyFullDetailReport.FromReplyItem(item);

        _ = report.RoeCashFlowNumerator.Should().Be(11M);
        _ = report.RoeCashFlowEquity.Should().Be(22M);
        _ = report.RoeOwnerEarningsNumerator.Should().Be(33M);
        _ = report.RoeOwnerEarningsEquity.Should().Be(44M);
        _ = report.RoeOwnerEarningsByYear.Should().Equal(
            new AnnualReturnOnEquity(new System.DateOnly(2025, 12, 31), 0.12M),
            new AnnualReturnOnEquity(new System.DateOnly(2024, 12, 31), null));
        _ = report.MostRecentAnnualReportDate.Should().Be(annual);
        _ = report.MostRecentQuarterlyReportDate.Should().Be(quarterly);
        _ = report.LastUpdatedDate.Should().BeNull();
        _ = report.CurRetainedEarningsEstimated.Should().BeTrue();
        _ = report.OldestRetainedEarningsEstimated.Should().BeTrue();
    }

    #endregion
}
