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
        decimal curShortTermDebt,
        decimal? curTotalShareholdersEquity,
        decimal? curBookValue,
        long curNumShares,
        decimal? averageNetCashFlow,
        decimal? averageOwnerEarnings,
        decimal? latestOwnerEarnings,
        decimal? curDividendsPaid,
        decimal? averageDividendsPaid,
        decimal? curAdjustedRetainedEarnings, // current retained earnings + dividends + share buybacks
        decimal? oldestRetainedEarnings,
        int numAnnualProcessedCashFlowReports,
        RoeInputs roe,
        ScoringOptions scoring,
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
        CurShortTermDebt = curShortTermDebt;
        CurTotalShareholdersEquity = curTotalShareholdersEquity;
        CurBookValue = curBookValue;
        CurNumShares = curNumShares;
        AverageNetCashFlow = averageNetCashFlow;
        AverageOwnerEarnings = averageOwnerEarnings;
        LatestOwnerEarnings = latestOwnerEarnings;
        CurDividendsPaid = curDividendsPaid;
        AverageDividendsPaid = averageDividendsPaid;
        CurAdjustedRetainedEarnings = curAdjustedRetainedEarnings;
        OldestRetainedEarnings = oldestRetainedEarnings;
        NumAnnualProcessedCashFlowReports = numAnnualProcessedCashFlowReports;
        RoeCashFlowNumerator = roe.CashFlowNumerator;
        RoeCashFlowEquity = roe.CashFlowEquity;
        RoeOwnerEarningsNumerator = roe.OwnerEarningsNumerator;
        RoeOwnerEarningsEquity = roe.OwnerEarningsEquity;
        RoeOwnerEarningsByYear = roe.OwnerEarningsByYear;
        // The check 16 settings are copied, so a later change to the options doesn't rescore this report.
        RoeConsistencyMinYears = scoring.RoeConsistencyMinYears;
        RoeConsistencyWindowYears = scoring.RoeConsistencyWindowYears;
        RoeConsistencyMinUsableYears = scoring.RoeConsistencyMinUsableYears;
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
        pricePerShare: item.PerSharePrice is null ? 0M : item.PerSharePrice, // unset: no quote (decision 20)
        curLongTermDebt: item.CurrentLongTermDebt,
        curShortTermDebt: item.CurrentShortTermDebt,
        curTotalShareholdersEquity: item.HasCurrentTotalShareholdersEquity ? item.CurrentTotalShareholdersEquity : null,
        curBookValue: item.HasCurrentBookValue ? item.CurrentBookValue : null,
        curNumShares: item.CurrentNumShares,
        averageNetCashFlow: item.HasAverageNetCashFlow ? item.AverageNetCashFlow : null,
        averageOwnerEarnings: item.HasAverageOwnerEarnings ? item.AverageOwnerEarnings : null,
        latestOwnerEarnings: item.HasLatestOwnerEarnings ? item.LatestOwnerEarnings : null,
        curDividendsPaid: item.HasCurrentDividendsPaid ? item.CurrentDividendsPaid : null,
        // An older server doesn't send the average: the latest year's dividends, as before R94.
        averageDividendsPaid: item.HasAverageDividendsPaid ? item.AverageDividendsPaid
            : item.HasCurrentDividendsPaid ? item.CurrentDividendsPaid : null,
        curAdjustedRetainedEarnings: item.HasCurrentAdjustedRetainedEarnings ? item.CurrentAdjustedRetainedEarnings : null,
        oldestRetainedEarnings: item.HasOldestRetainedEarnings ? item.OldestRetainedEarnings : null,
        numAnnualProcessedCashFlowReports: item.NumAnnualProcessedCashFlowReports,
        roe: new RoeInputs(
            CashFlowNumerator: item.HasRoeCashFlowNumerator ? item.RoeCashFlowNumerator : null,
            CashFlowEquity: item.HasRoeCashFlowEquity ? item.RoeCashFlowEquity : null,
            OwnerEarningsNumerator: item.HasRoeOwnerEarningsNumerator ? item.RoeOwnerEarningsNumerator : null,
            OwnerEarningsEquity: item.HasRoeOwnerEarningsEquity ? item.RoeOwnerEarningsEquity : null,
            OwnerEarningsByYear: [.. item.RoeOwnerEarningsByYear.Select(y => new AnnualReturnOnEquity(
                DateOnly.Parse(y.YearEnd, CultureInfo.InvariantCulture), y.Roe is null ? null : (decimal)y.Roe))]),
        // The scoring settings travel with each reply item; 0 comes from an older server that didn't send them.
        scoring: new ScoringOptions {
            RoeConsistencyMinYears = item.RoeConsistencyMinYears > 0 ? item.RoeConsistencyMinYears : ScoringOptions.DefaultRoeConsistencyMinYears,
            RoeConsistencyWindowYears = item.RoeConsistencyWindowYears > 0 ? item.RoeConsistencyWindowYears : ScoringOptions.DefaultRoeConsistencyWindowYears,
            RoeConsistencyMinUsableYears = item.RoeConsistencyMinUsableYears > 0 ? item.RoeConsistencyMinUsableYears : ScoringOptions.DefaultRoeConsistencyMinUsableYears,
        },
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
    /// <summary>Debt due within a year (X3).</summary>
    public decimal CurShortTermDebt { get; init; }
    // Null means the source data didn't have the figure (W1).
    public decimal? CurTotalShareholdersEquity { get; init; }
    public decimal? CurBookValue { get; init; }
    public long CurNumShares { get; init; }
    public decimal? AverageNetCashFlow { get; init; }
    public decimal? AverageOwnerEarnings { get; init; }
    /// <summary>The latest year's owner earnings; null when there is no usable latest year (V2).</summary>
    public decimal? LatestOwnerEarnings { get; init; }
    public decimal? CurDividendsPaid { get; init; }
    /// <summary>Dividends averaged over the same years as <see cref="AverageNetCashFlow"/> (R94).</summary>
    public decimal? AverageDividendsPaid { get; init; }
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
    /// <summary>Long-term plus short-term debt: what checks 1 (D/E) and 10 (D/B) use (X3, decision 5).</summary>
    public decimal CurTotalDebt => CurLongTermDebt + CurShortTermDebt;
    public decimal? DebtToEquityRatio => Utilities.DivOrNull(CurTotalDebt, CurTotalShareholdersEquity);
    public decimal? CurPriceToBookRatio => Utilities.DivOrNull(CurMarketCap, CurBookValue);
    /// <summary>Total debt (long-term + short-term since X3) over book value; the name is kept for the REST contract.</summary>
    public decimal? LongTermDebtToBookRatio => Utilities.DivOrNull(CurTotalDebt, CurBookValue);
    /// <summary>Net cash flow + dividends over the equity it was earned on (B11).</summary>
    public decimal? ReturnOnEquity_FromCashFlow => Utilities.DivOrNull(RoeCashFlowNumerator, RoeCashFlowEquity);
    /// <summary>Owner earnings over the equity they were earned on (B11).</summary>
    public decimal? ReturnOnEquity_FromOwnerEarnings => Utilities.DivOrNull(RoeOwnerEarningsNumerator, RoeOwnerEarningsEquity);
    public decimal? EstimatedNextYearBookValue_FromCashFlow => CurBookValue + AverageNetCashFlow;
    public decimal? EstimatedNextYearBookValue_FromOwnerEarnings => CurBookValue + AverageOwnerEarnings;
    /// <summary>
    /// Net cash flow is after dividends (a financing outflow) but before buybacks, so dividends are added back: cash
    /// generated for shareholders, however it is returned (decision 2). The dividends are averaged over the same years
    /// as the net cash flow, so a special dividend or a cut in the latest year doesn't skew the return (R94, decision 6).
    /// </summary>
    public decimal? EstimatedNextYearTotalReturnPercentage_FromCashFlow =>
        Utilities.DivOrNull(100M * (AverageNetCashFlow + AverageDividendsPaid), CurMarketCap);
    /// <summary>
    /// Owner earnings are pre-dividend, so all of it is the shareholders' return (decision 2). The lower of the average
    /// and the latest year, so a company whose earnings fell doesn't score like a steady one (V2).
    /// </summary>
    public decimal? EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings =>
        Utilities.DivOrNull(100M * ConservativeOwnerEarnings, CurMarketCap);

    /// <summary>The average owner earnings, or the latest year's when they are lower (V2); null without an average.</summary>
    private decimal? ConservativeOwnerEarnings =>
        LatestOwnerEarnings < AverageOwnerEarnings ? LatestOwnerEarnings : AverageOwnerEarnings;

    /// <summary>
    /// The highest price at which price-to-book is at most 3 and each est. return is at least 5% (20x its numerator).
    /// </summary>
    public decimal? MaxPrice {
        get {
            if (CurNumShares <= 0 || CurBookValue is not decimal bookValue
                || AverageNetCashFlow is not decimal netCashFlow || AverageDividendsPaid is not decimal dividends
                || ConservativeOwnerEarnings is not decimal ownerEarnings) {
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
    /// <summary>On the average alone: a weak latest year must not make an average that looks like wrong data pass (V2).</summary>
    public CheckResult DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig =>
        Check(Utilities.DivOrNull(100M * AverageOwnerEarnings, CurMarketCap), value => value < 40M);
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

    /// <summary>
    /// The newest report, annual or quarterly, is more than <see cref="ScoringOptions.StaleReportMonths"/> months old, so
    /// the score pairs old figures with today's price (V10). Without any report date it is unknown, and not stale.
    /// </summary>
    public bool HasStaleReports(DateTimeOffset nowUtc) {
        DateTimeOffset? newest = MostRecentAnnualReportDate;
        if (newest is null || MostRecentQuarterlyReportDate > newest)
            newest = MostRecentQuarterlyReportDate;
        return newest is DateTimeOffset date && date.UtcDateTime.Date < nowUtc.UtcDateTime.Date.AddMonths(-ScoringOptions.StaleReportMonths);
    }

    /// <summary>In the top list: a high enough score, on reports that are not stale.</summary>
    public bool IsTopScore(DateTimeOffset nowUtc) => OverallScore >= ScoringOptions.TopScoreCutoff && !HasStaleReports(nowUtc);

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
