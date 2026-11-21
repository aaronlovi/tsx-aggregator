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
        numAnnualProcessedCashFlowReports: 5,
        roeCashFlowNumerator: averageNetCashFlow,
        roeCashFlowEquity: 1_000M,
        roeOwnerEarningsNumerator: averageOwnerEarnings,
        roeOwnerEarningsEquity: 1_000M);

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
        curTotalShareholdersEquity: equity,
        curBookValue: bookValue,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings,
        curDividendsPaid: 0M,
        curAdjustedRetainedEarnings: 100M,
        oldestRetainedEarnings: 50M,
        numAnnualProcessedCashFlowReports: 5,
        roeCashFlowNumerator: averageNetCashFlow,
        roeCashFlowEquity: equity,
        roeOwnerEarningsNumerator: averageOwnerEarnings,
        roeOwnerEarningsEquity: equity);

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
        curTotalShareholdersEquity: equity,
        curBookValue: bookValue,
        curNumShares: 100,
        averageNetCashFlow: averageNetCashFlow,
        averageOwnerEarnings: averageOwnerEarnings,
        curDividendsPaid: curDividendsPaid,
        curAdjustedRetainedEarnings: curAdjustedRetainedEarnings,
        oldestRetainedEarnings: oldestRetainedEarnings,
        numAnnualProcessedCashFlowReports: 5,
        roeCashFlowNumerator: averageNetCashFlow,
        roeCashFlowEquity: equity,
        roeOwnerEarningsNumerator: averageOwnerEarnings,
        roeOwnerEarningsEquity: equity);

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
        _ = report.OverallScore.Should().Be(15);
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
        _ = report.OverallScore.Should().Be(10);
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

    #endregion
}
