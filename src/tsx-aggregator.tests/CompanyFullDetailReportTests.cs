using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

/// <summary>
/// Ratios, checks and max price in <see cref="CompanyFullDetailReport"/>. Dividends paid are positive,
/// as in the raw data (CashDividendsPaid, Scoring Bug Fix Plan section 2.5).
/// </summary>
public class CompanyFullDetailReportTests {
    /// <summary>Market cap 1,000 (100 shares at 10); book value and equity 1,000.</summary>
    private static CompanyFullDetailReport Report(
        decimal averageNetCashFlow, decimal averageOwnerEarnings, decimal curDividendsPaid) => new(
        exchange: "TSX",
        companySymbol: "TST",
        instrumentSymbol: "TST",
        companyName: "Test Co",
        instrumentName: "Test Co Common",
        pricePerShare: 10M,
        curLongTermDebt: 0M,
        curTotalShareholdersEquity: 1_000M,
        curBookValue: 1_000M,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings,
        curDividendsPaid: curDividendsPaid,
        curAdjustedRetainedEarnings: 100M,
        oldestRetainedEarnings: 50M,
        numAnnualProcessedCashFlowReports: 5);

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

        _ = report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough.Should().BeTrue();
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
        curTotalShareholdersEquity: equity,
        curBookValue: bookValue,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings,
        curDividendsPaid: 0M,
        curAdjustedRetainedEarnings: 100M,
        oldestRetainedEarnings: 50M,
        numAnnualProcessedCashFlowReports: 5);

    [Fact]
    public void NegativeEquity_FailsDebtToEquityAndBothRoeChecks() {
        // Debt-to-equity is 10 / -100 = -0.1 (< 0.5); ROE is -50 / -100 = 0.5 (> 0.10).
        var report = ReportWithEquity(equity: -100M, bookValue: 500M, averageNetCashFlow: -50M, averageOwnerEarnings: -60M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().BeFalse();
    }

    [Fact]
    public void ZeroEquity_FailsBothRoeChecks() {
        // DivSafe returns decimal.MaxValue for a zero denominator, which is > 0.10.
        var report = ReportWithEquity(equity: 0M, bookValue: 500M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().BeFalse();
    }

    [Fact]
    public void NegativeBookValue_FailsPriceToBookAndDebtToBook() {
        // Goodwill larger than equity: price-to-book 1,000 / -200 = -5 (<= 3); debt-to-book -0.05 (< 1).
        var report = ReportWithEquity(equity: 300M, bookValue: -200M);

        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().BeFalse();
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().BeFalse();
    }

    [Fact]
    public void NegativeBookValue_PositiveEquity_KeepsEquityChecks() {
        var report = ReportWithEquity(equity: 300M, bookValue: -200M);

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().BeTrue();
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().BeTrue();
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().BeTrue();
    }

    [Fact]
    public void ZeroBookValue_FailsPriceToBookAndDebtToBook() {
        var report = ReportWithEquity(equity: 300M, bookValue: 0M);

        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().BeFalse();
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().BeFalse();
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

        _ = report.DoesPassCheck_DebtToEquitySmallEnough.Should().BeTrue();
        _ = report.DoesPassCheck_PriceToBookSmallEnough.Should().BeTrue();
        _ = report.DoesPassCheck_DebtToBookRatioSmallEnough.Should().BeTrue();
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().BeTrue();
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().BeTrue();
    }

    #endregion
}
