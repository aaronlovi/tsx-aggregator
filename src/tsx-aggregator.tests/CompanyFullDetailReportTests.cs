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
}
