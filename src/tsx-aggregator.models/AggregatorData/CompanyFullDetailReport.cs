using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using tsx_aggregator.Services;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public class CompanyFullDetailReport {
    public CompanyFullDetailReport(
        string exchange,
        string companySymbol,
        string instrumentSymbol,
        string companyName,
        string instrumentName,
        decimal pricePerShare,
        decimal curLongTermDebt,
        decimal? curTotalShareholdersEquity,
        decimal? curBookValue,
        long curNumShares,
        decimal? averageNetCashFlow,
        decimal? averageOwnerEarnings,
        decimal? curDividendsPaid,
        decimal? curAdjustedRetainedEarnings, // current retained earnings + dividends + share buybacks
        decimal? oldestRetainedEarnings,
        int numAnnualProcessedCashFlowReports,
        decimal? roeCashFlowNumerator,
        decimal? roeCashFlowEquity,
        decimal? roeOwnerEarningsNumerator,
        decimal? roeOwnerEarningsEquity,
        IReadOnlyList<AnnualReturnOnEquity>? roeOwnerEarningsByYear = null,
        int roeConsistencyMinYears = ScoringOptions.DefaultRoeConsistencyMinYears,
        int roeConsistencyWindowYears = ScoringOptions.DefaultRoeConsistencyWindowYears,
        int roeConsistencyMinUsableYears = ScoringOptions.DefaultRoeConsistencyMinUsableYears,
        DateTimeOffset? mostRecentAnnualReportDate = null,
        DateTimeOffset? mostRecentQuarterlyReportDate = null,
        DateTimeOffset? lastUpdatedDate = null,
        bool curRetainedEarningsEstimated = false,
        bool oldestRetainedEarningsEstimated = false) {

        Exchange = exchange;
        CompanySymbol = companySymbol;
        InstrumentSymbol = instrumentSymbol;
        CompanyName = companyName;
        InstrumentName = instrumentName;
        PricePerShare = pricePerShare;
        CurLongTermDebt = curLongTermDebt;
        CurTotalShareholdersEquity = curTotalShareholdersEquity;
        CurBookValue = curBookValue;
        CurNumShares = curNumShares;
        AverageNetCashFlow = averageNetCashFlow;
        AverageOwnerEarnings = averageOwnerEarnings;
        CurDividendsPaid = curDividendsPaid;
        CurAdjustedRetainedEarnings = curAdjustedRetainedEarnings;
        OldestRetainedEarnings = oldestRetainedEarnings;
        NumAnnualProcessedCashFlowReports = numAnnualProcessedCashFlowReports;
        RoeCashFlowNumerator = roeCashFlowNumerator;
        RoeCashFlowEquity = roeCashFlowEquity;
        RoeOwnerEarningsNumerator = roeOwnerEarningsNumerator;
        RoeOwnerEarningsEquity = roeOwnerEarningsEquity;
        RoeOwnerEarningsByYear = roeOwnerEarningsByYear;
        RoeConsistencyMinYears = roeConsistencyMinYears;
        RoeConsistencyWindowYears = roeConsistencyWindowYears;
        RoeConsistencyMinUsableYears = roeConsistencyMinUsableYears;
        MostRecentAnnualReportDate = mostRecentAnnualReportDate;
        MostRecentQuarterlyReportDate = mostRecentQuarterlyReportDate;
        LastUpdatedDate = lastUpdatedDate;
        CurRetainedEarningsEstimated = curRetainedEarningsEstimated;
        OldestRetainedEarningsEstimated = oldestRetainedEarningsEstimated;
    }

    /// <summary>
    /// Builds the report from a gRPC reply item. The one mapping used by the REST API, the alert service and the
    /// dashboard; unset optional fields become null.
    /// </summary>
    public static CompanyFullDetailReport FromReplyItem(GetStocksDataReplyItem item) => new(
        exchange: item.Exchange,
        companySymbol: item.CompanySymbol,
        instrumentSymbol: item.InstrumentSymbol,
        companyName: item.CompanyName,
        instrumentName: item.InstrumentName,
        pricePerShare: item.PerSharePrice,
        curLongTermDebt: item.CurrentLongTermDebt,
        curTotalShareholdersEquity: item.HasCurrentTotalShareholdersEquity ? item.CurrentTotalShareholdersEquity : null,
        curBookValue: item.HasCurrentBookValue ? item.CurrentBookValue : null,
        curNumShares: item.CurrentNumShares,
        averageNetCashFlow: item.HasAverageNetCashFlow ? item.AverageNetCashFlow : null,
        averageOwnerEarnings: item.HasAverageOwnerEarnings ? item.AverageOwnerEarnings : null,
        curDividendsPaid: item.HasCurrentDividendsPaid ? item.CurrentDividendsPaid : null,
        curAdjustedRetainedEarnings: item.HasCurrentAdjustedRetainedEarnings ? item.CurrentAdjustedRetainedEarnings : null,
        oldestRetainedEarnings: item.HasOldestRetainedEarnings ? item.OldestRetainedEarnings : null,
        numAnnualProcessedCashFlowReports: item.NumAnnualProcessedCashFlowReports,
        roeCashFlowNumerator: item.HasRoeCashFlowNumerator ? item.RoeCashFlowNumerator : null,
        roeCashFlowEquity: item.HasRoeCashFlowEquity ? item.RoeCashFlowEquity : null,
        roeOwnerEarningsNumerator: item.HasRoeOwnerEarningsNumerator ? item.RoeOwnerEarningsNumerator : null,
        roeOwnerEarningsEquity: item.HasRoeOwnerEarningsEquity ? item.RoeOwnerEarningsEquity : null,
        roeOwnerEarningsByYear: [.. item.RoeOwnerEarningsByYear.Select(y => new AnnualReturnOnEquity(
            DateOnly.Parse(y.YearEnd, CultureInfo.InvariantCulture), y.Roe is null ? null : (decimal)y.Roe))],
        roeConsistencyMinYears: item.RoeConsistencyMinYears > 0 ? item.RoeConsistencyMinYears : ScoringOptions.DefaultRoeConsistencyMinYears,
        roeConsistencyWindowYears: item.RoeConsistencyWindowYears > 0 ? item.RoeConsistencyWindowYears : ScoringOptions.DefaultRoeConsistencyWindowYears,
        roeConsistencyMinUsableYears: item.RoeConsistencyMinUsableYears > 0 ? item.RoeConsistencyMinUsableYears : ScoringOptions.DefaultRoeConsistencyMinUsableYears,
        mostRecentAnnualReportDate: item.MostRecentAnnualReportDate?.ToDateTimeOffset(),
        mostRecentQuarterlyReportDate: item.MostRecentQuarterlyReportDate?.ToDateTimeOffset(),
        lastUpdatedDate: item.LastUpdatedDate?.ToDateTimeOffset(),
        curRetainedEarningsEstimated: item.CurrentRetainedEarningsEstimated,
        oldestRetainedEarningsEstimated: item.OldestRetainedEarningsEstimated);

    public string Exchange { get; init; }
    public string CompanySymbol { get; init; }
    public string InstrumentSymbol { get; init; }
    public string CompanyName { get; init; }
    public string InstrumentName { get; init; }
    public decimal PricePerShare { get; init; }
    public decimal CurLongTermDebt { get; init; }
    // Null means the source data didn't have the figure (W1).
    public decimal? CurTotalShareholdersEquity { get; init; }
    public decimal? CurBookValue { get; init; }
    public long CurNumShares { get; init; }
    public decimal? AverageNetCashFlow { get; init; }
    public decimal? AverageOwnerEarnings { get; init; }
    public decimal? CurDividendsPaid { get; init; }
    public decimal? CurAdjustedRetainedEarnings { get; init; }
    public decimal? OldestRetainedEarnings { get; init; }
    public int NumAnnualProcessedCashFlowReports { get; init; }
    // ROE inputs (B11, decisions 8 and 24): numerator and equity sums over matched annual years, from CompanyReport.
    // Not part of the REST contract: the API serves the ROE ratios.
    [JsonIgnore] public decimal? RoeCashFlowNumerator { get; init; }
    [JsonIgnore] public decimal? RoeCashFlowEquity { get; init; }
    [JsonIgnore] public decimal? RoeOwnerEarningsNumerator { get; init; }
    [JsonIgnore] public decimal? RoeOwnerEarningsEquity { get; init; }
    /// <summary>ROE (OE) of each usable year, most recent first (C16); null when the stored report predates it.</summary>
    [JsonIgnore] public IReadOnlyList<AnnualReturnOnEquity>? RoeOwnerEarningsByYear { get; init; }
    /// <summary>Check 16 passes with ROE (OE) over 10% in at least this many of the last <see cref="RoeConsistencyWindowYears"/>.</summary>
    public int RoeConsistencyMinYears { get; init; }
    public int RoeConsistencyWindowYears { get; init; }
    /// <summary>Fewer usable years than this makes check 16 n/a.</summary>
    public int RoeConsistencyMinUsableYears { get; init; }
    public DateTimeOffset? MostRecentAnnualReportDate { get; init; }
    public DateTimeOffset? MostRecentQuarterlyReportDate { get; init; }
    public DateTimeOffset? LastUpdatedDate { get; init; }
    /// <summary>
    /// The provider's balance sheet had no retained earnings, so the figure behind CurAdjustedRetainedEarnings was
    /// estimated with net income - dividends from the nearest balance sheet that had it (decision 23).
    /// </summary>
    public bool CurRetainedEarningsEstimated { get; init; }
    /// <summary>As <see cref="CurRetainedEarningsEstimated"/>, for OldestRetainedEarnings.</summary>
    public bool OldestRetainedEarningsEstimated { get; init; }

    // Calculated Properties. Null when an input is null, or a denominator is 0.

    /// <summary>Null without a quote (price 0 or less) or share count (decision 20).</summary>
    public decimal? CurMarketCap => PricePerShare > 0 && CurNumShares > 0 ? CurNumShares * PricePerShare : null;
    public decimal? DebtToEquityRatio => Utilities.DivOrNull(CurLongTermDebt, CurTotalShareholdersEquity);
    public decimal? CurPriceToBookRatio => Utilities.DivOrNull(CurMarketCap, CurBookValue);
    public decimal? LongTermDebtToBookRatio => Utilities.DivOrNull(CurLongTermDebt, CurBookValue);
    /// <summary>Net cash flow + dividends over the equity it was earned on (B11).</summary>
    public decimal? ReturnOnEquity_FromCashFlow => Utilities.DivOrNull(RoeCashFlowNumerator, RoeCashFlowEquity);
    /// <summary>Owner earnings over the equity they were earned on (B11).</summary>
    public decimal? ReturnOnEquity_FromOwnerEarnings => Utilities.DivOrNull(RoeOwnerEarningsNumerator, RoeOwnerEarningsEquity);
    public decimal? EstimatedNextYearBookValue_FromCashFlow => CurBookValue + AverageNetCashFlow;
    public decimal? EstimatedNextYearBookValue_FromOwnerEarnings => CurBookValue + AverageOwnerEarnings;
    /// <summary>
    /// Net cash flow is after dividends (a financing outflow) but before buybacks, so dividends are added back: cash
    /// generated for shareholders, however it is returned (decision 2).
    /// </summary>
    public decimal? EstimatedNextYearTotalReturnPercentage_FromCashFlow =>
        Utilities.DivOrNull(100M * (AverageNetCashFlow + CurDividendsPaid), CurMarketCap);
    /// <summary>Owner earnings are pre-dividend, so all of it is the shareholders' return (decision 2).</summary>
    public decimal? EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings =>
        Utilities.DivOrNull(100M * AverageOwnerEarnings, CurMarketCap);

    /// <summary>
    /// The highest price at which price-to-book is at most 3 and each est. return is at least 5% (20x its numerator).
    /// </summary>
    public decimal? MaxPrice {
        get {
            if (CurNumShares <= 0 || CurBookValue is not decimal bookValue
                || AverageNetCashFlow is not decimal netCashFlow || CurDividendsPaid is not decimal dividends
                || AverageOwnerEarnings is not decimal ownerEarnings) {
                return null;
            }

            decimal maxPriceSoFar = 3M * bookValue;
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * (netCashFlow + dividends));
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * ownerEarnings);
            return maxPriceSoFar / CurNumShares;
        }
    }

    public decimal? PercentageUpside => PricePerShare > 0 && MaxPrice is decimal maxPrice
        ? (maxPrice - PricePerShare) / PricePerShare * 100M
        : null;

    // Checks (W1): NotAvailable when an input is missing; otherwise Fail when the denominator is equity or book
    // value and it is 0 or less (the B7 sign rule, decision 4); otherwise the threshold decides.

    public CheckResult DoesPassCheck_DebtToEquitySmallEnough =>
        CheckWithPositiveDenominator(CurTotalShareholdersEquity, DebtToEquityRatio, ratio => ratio < 0.5M);
    public CheckResult DoesPassCheck_BookValueBigEnough => Check(CurBookValue, bookValue => bookValue > 150_000_000M);
    public CheckResult DoesPassCheck_PriceToBookSmallEnough =>
        CurMarketCap is null ? CheckResult.NotAvailable
        : CheckWithPositiveDenominator(CurBookValue, CurPriceToBookRatio, ratio => ratio <= 3M);
    public CheckResult DoesPassCheck_AvgCashFlow_Positive => Check(AverageNetCashFlow, value => value > 0);
    public CheckResult DoesPassCheck_AvgOwnerEarningsPositive => Check(AverageOwnerEarnings, value => value > 0);
    public CheckResult DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough =>
        Check(EstimatedNextYearTotalReturnPercentage_FromCashFlow, value => value > 5M);
    public CheckResult DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough =>
        Check(EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings, value => value > 5M);
    public CheckResult DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig =>
        Check(EstimatedNextYearTotalReturnPercentage_FromCashFlow, value => value < 40M);
    public CheckResult DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig =>
        Check(EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings, value => value < 40M);
    public CheckResult DoesPassCheck_DebtToBookRatioSmallEnough =>
        CheckWithPositiveDenominator(CurBookValue, LongTermDebtToBookRatio, ratio => ratio < 1M);
    public CheckResult DoesPassCheck_RetainedEarningsPositive => Check(CurAdjustedRetainedEarnings, value => value > 0);
    public CheckResult DoesPassCheck_IsHistoryLongEnough =>
        NumAnnualProcessedCashFlowReports >= 4 ? CheckResult.Pass : CheckResult.Fail;
    /// <summary>The 13th check (its name predates the DoesPassCheck_ convention).</summary>
    public CheckResult DidAdjustedRetainedEarningsIncrease =>
        CurAdjustedRetainedEarnings is decimal current && OldestRetainedEarnings is decimal oldest
            ? (current > oldest ? CheckResult.Pass : CheckResult.Fail)
            : CheckResult.NotAvailable;
    public CheckResult DoesPassCheck_ROE_FromCashFlow_BigEnough =>
        CheckWithPositiveDenominator(RoeCashFlowEquity, ReturnOnEquity_FromCashFlow, roe => roe > 0.10M);
    public CheckResult DoesPassCheck_ROE_FromOwnerEarnings_BigEnough =>
        CheckWithPositiveDenominator(RoeOwnerEarningsEquity, ReturnOnEquity_FromOwnerEarnings, roe => roe > 0.10M);

    /// <summary>
    /// How many usable years check 16 looks at: the last <see cref="RoeConsistencyWindowYears"/>, or all of them when
    /// there are fewer. Null when there are fewer than <see cref="RoeConsistencyMinUsableYears"/> (C16, decision 28).
    /// </summary>
    public int? RoeOwnerEarningsYearsConsidered =>
        RoeOwnerEarningsByYear is { } years && Math.Min(years.Count, RoeConsistencyWindowYears) is int considered
            && considered >= RoeConsistencyMinUsableYears
                ? considered
                : null;
    /// <summary>
    /// How many of the years considered had ROE (OE) over 10% (a year with average equity 0 or less doesn't count).
    /// </summary>
    public int? RoeOwnerEarningsYearsAbove10 =>
        RoeOwnerEarningsYearsConsidered is int considered
            ? RoeOwnerEarningsByYear!.Take(considered).Count(y => y.Roe > 0.10M)
            : null;
    /// <summary>
    /// How many of the years considered must be over 10%: N of the full window, or the same number of misses
    /// (M - N) over fewer years, at least 1.
    /// </summary>
    public int? RoeOwnerEarningsYearsRequired =>
        RoeOwnerEarningsYearsConsidered is int considered
            ? Math.Max(1, considered - (RoeConsistencyWindowYears - RoeConsistencyMinYears))
            : null;
    /// <summary>Check 16: ROE (OE) over 10% in at least N of the last M years, scaled to fewer usable years (C16).</summary>
    public CheckResult DoesPassCheck_ROE_FromOwnerEarnings_Consistent =>
        RoeOwnerEarningsYearsAbove10 is int above && RoeOwnerEarningsYearsRequired is int required
            ? (above >= required ? CheckResult.Pass : CheckResult.Fail)
            : CheckResult.NotAvailable;

    /// <summary>The 16 checks, in the order the score lists them (16 added by C16). Not serialized: each has its own field.</summary>
    [JsonIgnore]
    public CheckResult[] AllChecks => [
        DoesPassCheck_DebtToEquitySmallEnough,
        DoesPassCheck_BookValueBigEnough,
        DoesPassCheck_PriceToBookSmallEnough,
        DoesPassCheck_AvgCashFlow_Positive,
        DoesPassCheck_AvgOwnerEarningsPositive,
        DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough,
        DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough,
        DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig,
        DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig,
        DoesPassCheck_DebtToBookRatioSmallEnough,
        DoesPassCheck_RetainedEarningsPositive,
        DoesPassCheck_IsHistoryLongEnough,
        DidAdjustedRetainedEarningsIncrease,
        DoesPassCheck_ROE_FromCashFlow_BigEnough,
        DoesPassCheck_ROE_FromOwnerEarnings_BigEnough,
        DoesPassCheck_ROE_FromOwnerEarnings_Consistent,
    ];

    public bool DoesPassCheck_Overall => AllChecks.All(check => check == CheckResult.Pass);
    /// <summary>Counts Pass only: NotAvailable earns nothing, like Fail, so the cutoff keeps its meaning.</summary>
    public int OverallScore => AllChecks.Count(check => check == CheckResult.Pass);
    public int NotAvailableCount => AllChecks.Count(check => check == CheckResult.NotAvailable);

    private static CheckResult Check(decimal? value, Func<decimal, bool> passes) =>
        value is decimal v ? (passes(v) ? CheckResult.Pass : CheckResult.Fail) : CheckResult.NotAvailable;

    private static CheckResult CheckWithPositiveDenominator(decimal? denominator, decimal? ratio, Func<decimal, bool> passes) {
        if (denominator is null)
            return CheckResult.NotAvailable;
        if (denominator <= 0)
            return CheckResult.Fail;
        return Check(ratio, passes);
    }
}
