using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.models;

public class CompanyReport
{
    private readonly SortedDictionary<DateOnly, RawReportDataMap> _annualRawCashFlowReports;
    private readonly SortedDictionary<DateOnly, RawReportDataMap> _annualRawIncomeStatements;
    private readonly SortedDictionary<DateOnly, RawReportDataMap> _annualRawBalanceSheets;

    private readonly SortedDictionary<DateOnly, RawReportDataMap> _nonAnnualRawCashFlowReports;
    private readonly SortedDictionary<DateOnly, RawReportDataMap> _nonAnnualRawIncomeStatements;
    private readonly SortedDictionary<DateOnly, RawReportDataMap> _nonAnnualRawBalanceSheets;

    private readonly SortedDictionary<DateOnly, CashFlowItem> _annualProcessedCashFlowItems;

    // An opening annual balance sheet counts for ROE only if it is about a year before the closing one (decision 24).
    private const int MinDaysBetweenAnnualBalanceSheets = 330;
    private const int MaxDaysBetweenAnnualBalanceSheets = 400;

    // Long-term debt, preferring the figure that includes capital leases. One list, so the current figure (check 1)
    // and the balance sheet debt change (the B3 fallback for net debt issuance) can't drift apart.
    private static readonly string[] LongTermDebtKeys = ["LongTermDebtAndCapitalLeaseObligation", "LongTermDebt"];

    // Dates of the report sets the current and oldest balance sheet figures come from.
    private DateOnly? _mostRecentReportDate;
    private DateOnly? _oldestReportDate;

    public CompanyReport(string symbol, string name, string exchange, long curNumShares)
    {
        Symbol = symbol;
        Name = name;
        Exchange = exchange;
        CurNumShares = curNumShares;
        _annualRawCashFlowReports = [];
        _annualRawIncomeStatements = [];
        _annualRawBalanceSheets = [];
        _nonAnnualRawCashFlowReports = [];
        _nonAnnualRawIncomeStatements = [];
        _nonAnnualRawBalanceSheets = [];
        _annualProcessedCashFlowItems = [];
    }

    #region PUBLIC PROPERTIES

    public long CurNumShares { get; init; }

    /// <summary>
    /// The currency of the most recent balance sheet (e.g. "CAD", "USD"); null if not known (raw reports stored before
    /// review X1). Not used in scoring yet: kept so the CAD/USD mismatch with the CAD price can be measured.
    /// </summary>
    public string? ReportingCurrency { get; private set; }
    public string Symbol { get; init; }
    public string Name { get; init; }
    public string Exchange { get; init; }
    // Null means the source data didn't have the figure (W1). Goodwill, intangibles and long-term debt stay 0 when
    // missing (decisions 6 and 7).
    public decimal? CurTotalShareholdersEquity { get; set; }
    public decimal CurGoodwill { get; set; }
    public decimal CurIntangibles { get; set; }
    public decimal CurLongTermDebt { get; set; }
    /// <summary>From the most recent annual cash flow; null when there is none, 0 when it has no dividends key.</summary>
    public decimal? CurDividendsPaid { get; set; }
    public decimal? CurRetainedEarnings { get; set; }
    /// <summary>
    /// True when CurRetainedEarnings isn't from the most recent balance sheet (which lacks it) but estimated from the
    /// nearest earlier one with net income - dividends (decision 23).
    /// </summary>
    public bool CurRetainedEarningsEstimated { get; private set; }
    /// <summary>Null when the current retained earnings are missing.</summary>
    public decimal? CurAdjustedRetainedEarnings { get; set; }
    public decimal? OldestRetainedEarnings { get; set; }
    /// <summary>True when OldestRetainedEarnings is estimated back from the next balance sheet that has it (decision 23).</summary>
    public bool OldestRetainedEarningsEstimated { get; private set; }
    /// <summary>Equity less goodwill and intangibles; null when equity is missing.</summary>
    public decimal? CurBookValue => CurTotalShareholdersEquity - (CurGoodwill + CurIntangibles);
    /// <summary>Null when there are no annual cash flow statements.</summary>
    public decimal? AverageNetCashFlow =>
        _annualProcessedCashFlowItems.Count == 0
            ? null
            : _annualProcessedCashFlowItems.Values.Sum(item => item.NetCashFlow) / _annualProcessedCashFlowItems.Count;
    /// <summary>
    /// Years without capex data are left out (decision 15). Null when no year is left, or there are no annual
    /// cash flow statements.
    /// </summary>
    public decimal? AverageOwnerEarnings
    {
        get
        {
            List<CashFlowItem> usableItems = [.. _annualProcessedCashFlowItems.Values.Where(item => item.IsUsableForOwnerEarnings)];
            return usableItems.Count == 0 ? null : usableItems.Sum(item => item.OwnerEarnings) / usableItems.Count;
        }
    }
    public int NumAnnualProcessedCashFlowReports => _annualProcessedCashFlowItems.Count;
    // Return on equity inputs (B11, decisions 8 and 24): a numerator sum and an equity sum per ROE, so the checks can
    // fail a non-positive equity. Null when there is no figure to use.
    /// <summary>Net cash flow + dividends over the ROE years (pre-dividend, like owner earnings).</summary>
    public decimal? RoeCashFlowNumerator { get; private set; }
    /// <summary>Equity the <see cref="RoeCashFlowNumerator"/> years were earned on.</summary>
    public decimal? RoeCashFlowEquity { get; private set; }
    /// <summary>Owner earnings over the ROE years that count for owner earnings (decision 15).</summary>
    public decimal? RoeOwnerEarningsNumerator { get; private set; }
    /// <summary>Equity the <see cref="RoeOwnerEarningsNumerator"/> years were earned on.</summary>
    public decimal? RoeOwnerEarningsEquity { get; private set; }
    /// <summary>
    /// ROE (OE) of each year with closing equity that counts for owner earnings, most recent first: the years
    /// behind check 16 (C16, decision 28).
    /// </summary>
    public IReadOnlyList<AnnualReturnOnEquity> RoeOwnerEarningsByYear { get; private set; } = [];
    [JsonIgnore] public string ToShortString => $"{Symbol}/{Name}";

    #endregion

    #region PUBLIC API

    public void AddBalanceSheet(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt) =>
        _ = ByPeriodType(rptPeriodType, _annualRawBalanceSheets, _nonAnnualRawBalanceSheets)?.TryAdd(reportDate, rpt);

    public void AddCashFlowStatement(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt) =>
        _ = ByPeriodType(rptPeriodType, _annualRawCashFlowReports, _nonAnnualRawCashFlowReports)?.TryAdd(reportDate, rpt);

    public void AddIncomeStatement(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt) =>
        _ = ByPeriodType(rptPeriodType, _annualRawIncomeStatements, _nonAnnualRawIncomeStatements)?.TryAdd(reportDate, rpt);

    // A second report for a date already added is ignored.
    public void AddAnnualIncomeStatement(DateOnly reportDate, RawReportDataMap rpt) => _ = _annualRawIncomeStatements.TryAdd(reportDate, rpt);
    public void AddAnnualBalanceSheet(DateOnly reportDate, RawReportDataMap rpt) => _ = _annualRawBalanceSheets.TryAdd(reportDate, rpt);
    public void AddAnnualCashFlowStatement(DateOnly reportDate, RawReportDataMap rpt) => _ = _annualRawCashFlowReports.TryAdd(reportDate, rpt);
    public void AddNonAnnualIncomeStatement(DateOnly reportDate, RawReportDataMap rpt) => _ = _nonAnnualRawIncomeStatements.TryAdd(reportDate, rpt);
    public void AddNonAnnualBalanceSheet(DateOnly reportDate, RawReportDataMap rpt) => _ = _nonAnnualRawBalanceSheets.TryAdd(reportDate, rpt);
    public void AddNonAnnualCashFlowStatement(DateOnly reportDate, RawReportDataMap rpt) => _ = _nonAnnualRawCashFlowReports.TryAdd(reportDate, rpt);

    public void ProcessReports(IList<string> warnings)
    {
        TransformOldestFinancialReports(warnings);
        TransformMostRecentFinancialReports(warnings);
        TransformMostRecentAnnualDividends();
        TransformRawFinancialReports(warnings);
        CalcReturnOnEquityInputs();
    }

    #endregion

    #region PRIVATE HELPER METHODS

    private void TransformOldestFinancialReports(IList<string> warnings)
    {
        // The earliest complete report set; an annual set wins a tie with a non-annual one. Every balance sheet
        // without a matching cash flow or income statement is warned about.
        if (FindReportSet((candidate, pick) => candidate <= pick, warnings) is not (DateOnly oldestReportDate, var oldestBalanceSheet)
            || oldestReportDate == DateOnly.MaxValue) // The old loops' "none found" sentinel, kept as it was
        {
            return;
        }

        _oldestReportDate = oldestReportDate;
        if (oldestBalanceSheet.HasValue("RetainedEarnings"))
            OldestRetainedEarnings = oldestBalanceSheet["RetainedEarnings"]!.Value;
        else
            LogMissingPropertyWarning(oldestReportDate, "RetainedEarnings", warnings);
    }

    /// <summary>
    /// The complete report set (a balance sheet with a cash flow and an income statement of the same date) that
    /// <paramref name="replaces"/> picks: non-annual sets oldest first, then annual sets oldest first, each one
    /// replacing the pick so far when replaces(its date, the pick's date) is true. Null when there is no complete
    /// set. With <paramref name="warnings"/>, every balance sheet missing a matching statement is warned about.
    /// </summary>
    private (DateOnly Date, RawReportDataMap BalanceSheet)? FindReportSet(
        Func<DateOnly, DateOnly, bool> replaces, IList<string>? warnings)
    {
        List<(DateOnly Date, RawReportDataMap BalanceSheet)> nonAnnual = CompleteReportSets(
            _nonAnnualRawBalanceSheets, _nonAnnualRawCashFlowReports, _nonAnnualRawIncomeStatements, "Non-annual", warnings);
        List<(DateOnly Date, RawReportDataMap BalanceSheet)> annual = CompleteReportSets(
            _annualRawBalanceSheets, _annualRawCashFlowReports, _annualRawIncomeStatements, "Annual", warnings);

        (DateOnly Date, RawReportDataMap BalanceSheet)? pick = null;
        foreach ((DateOnly Date, RawReportDataMap BalanceSheet) set in nonAnnual.Concat(annual))
        {
            if (pick is not (DateOnly pickDate, _) || replaces(set.Date, pickDate))
                pick = set;
        }
        return pick;
    }

    /// <summary>The balance sheets, oldest first, that have a cash flow and an income statement of the same date.</summary>
    private static List<(DateOnly Date, RawReportDataMap BalanceSheet)> CompleteReportSets(
        SortedDictionary<DateOnly, RawReportDataMap> balanceSheets,
        SortedDictionary<DateOnly, RawReportDataMap> cashFlowStatements,
        SortedDictionary<DateOnly, RawReportDataMap> incomeStatements,
        string periodLabel,
        IList<string>? warnings)
    {
        List<(DateOnly Date, RawReportDataMap BalanceSheet)> sets = [];
        foreach ((DateOnly reportDate, RawReportDataMap balanceSheet) in balanceSheets)
        {
            bool foundMatchingCashFlow = cashFlowStatements.ContainsKey(reportDate);
            bool foundMatchingIncome = incomeStatements.ContainsKey(reportDate);
            if (!foundMatchingCashFlow)
                warnings?.Add($"{periodLabel} balance sheet for {reportDate} but no matching cash flow statement");
            if (!foundMatchingIncome)
                warnings?.Add($"{periodLabel} balance sheet for {reportDate} but no matching income statement");
            if (foundMatchingCashFlow && foundMatchingIncome)
                sets.Add((reportDate, balanceSheet));
        }
        return sets;
    }

    /// <summary>The annual or non-annual collection for a period type; null for an undefined period type.</summary>
    private static SortedDictionary<DateOnly, RawReportDataMap>? ByPeriodType(
        ReportPeriodTypes rptPeriodType,
        SortedDictionary<DateOnly, RawReportDataMap> annual,
        SortedDictionary<DateOnly, RawReportDataMap> nonAnnual) =>
        rptPeriodType switch
        {
            ReportPeriodTypes.Annual => annual,
            ReportPeriodTypes.Quarterly or ReportPeriodTypes.SemiAnnual => nonAnnual, // Semi-annual is treated as quarterly
            _ => null,
        };

    /// <summary>
    /// Current dividends come from the most recent annual cash flow statement, so they cover a full year like the
    /// annual averages they are combined with. Quarterly dividend figures are unreliable (decision 18). 0 when that
    /// statement has no CashDividendsPaid.
    /// </summary>
    private void TransformMostRecentAnnualDividends()
    {
        if (_annualRawCashFlowReports.Count == 0)
            return;

        RawReportDataMap mostRecentAnnualCashFlow = _annualRawCashFlowReports.Values.Last();
        CurDividendsPaid = mostRecentAnnualCashFlow["CashDividendsPaid"] ?? 0;
    }

    private void TransformMostRecentFinancialReports(IList<string> warnings)
    {
        // The latest complete report set; an annual set wins a tie with a non-annual one.
        if (FindReportSet((candidate, pick) => candidate >= pick, warnings: null) is not (DateOnly mostRecentReportDate, var mostRecentBalanceSheet)
            || mostRecentReportDate == DateOnly.MinValue) // The old loops' "none found" sentinel, kept as it was
        {
            return;
        }

        _mostRecentReportDate = mostRecentReportDate;
        ReportingCurrency = mostRecentBalanceSheet.Currency;

        if (mostRecentBalanceSheet.HasValue("StockholdersEquity"))
            CurTotalShareholdersEquity = mostRecentBalanceSheet["StockholdersEquity"]!.Value;
        else
            warnings.Add("TransformMostRecentFinancialReports: missing property current StockholdersEquity");

        // Goodwill, else the combined figure less other intangibles, else all of the combined figure. A missing figure
        // leaves the property at 0 (decisions 6 and 7). A lifted subtraction is null when either side is missing.
        decimal? combinedGoodwillAndIntangibles = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"];
        if ((mostRecentBalanceSheet["Goodwill"]
                ?? (combinedGoodwillAndIntangibles - mostRecentBalanceSheet["OtherIntangibleAssets"])
                ?? combinedGoodwillAndIntangibles) is decimal goodwill)
            CurGoodwill = goodwill;
        else
            warnings.Add("TransformMostRecentFinancialReports: missing property current Goodwill");

        // Other intangibles, else the combined figure less goodwill (no "all of the combined figure" fallback here).
        if ((mostRecentBalanceSheet["OtherIntangibleAssets"]
                ?? (combinedGoodwillAndIntangibles - mostRecentBalanceSheet["Goodwill"])) is decimal intangibles)
            CurIntangibles = intangibles;
        else
            warnings.Add("Most recent figures: missing property current Intangibles");

        if (mostRecentBalanceSheet.FirstPresent(LongTermDebtKeys) is decimal longTermDebt)
            CurLongTermDebt = longTermDebt;
        else
            warnings.Add("Most recent figures: missing property current LongTermDebt");

        if (mostRecentBalanceSheet.HasValue("RetainedEarnings"))
            CurRetainedEarnings = mostRecentBalanceSheet["RetainedEarnings"]!.Value;
        else
            warnings.Add("Most recent figures: missing property current RetainedEarnings");
    }

    private void TransformRawFinancialReports(IList<string> warnings)
    {
        decimal totalAnnualDividendsPaid = 0;
        decimal totalAnnualCommonStockIssuance = 0;
        decimal totalAnnualPreferredStockIssuance = 0;

        foreach ((DateOnly reportDate, RawReportDataMap rawCashFlowReport) in _annualRawCashFlowReports)
        {
            if (_annualProcessedCashFlowItems.ContainsKey(reportDate))
                continue;

            var cashFlowReport = new CashFlowItem();

            // Only years after the oldest balance sheet, up to the current one, are added back: dividends and
            // buybacks on or before the oldest snapshot are already in OldestRetainedEarnings (review P8), and later
            // ones are not in CurRetainedEarnings. The same window as EstimateMissingRetainedEarnings.
            bool inAdjustmentWindow = (_oldestReportDate is not DateOnly oldest || reportDate > oldest)
                && (_mostRecentReportDate is not DateOnly current || reportDate <= current);

            // Each fallback (`??`) is evaluated, and warns, only when the figures before it are missing.
            if ((rawCashFlowReport["NetIncomeFromContinuingOperations"]
                    ?? CalcIncomeStatement_NetIncomeFromContinuingOperations(reportDate, warnings)) is decimal netIncome)
                cashFlowReport.NetIncomeFromContinuingOperations = netIncome;
            else
                LogMissingPropertyWarning(reportDate, "NetIncomeFromContinuingOperations", warnings);

            if ((rawCashFlowReport["ChangesInCash"]
                    ?? (rawCashFlowReport["EndCashPosition"] - rawCashFlowReport["BeginningCashPosition"])) is decimal grossCashFlow)
                cashFlowReport.GrossCashFlow = grossCashFlow;
            else
                LogMissingPropertyWarning(reportDate, "GrossCashFlow", warnings);

            if ((rawCashFlowReport.FirstPresent("NetIssuancePaymentsOfDebt", "NetLongTermDebtIssuance")
                    ?? CalcConsecutiveBalanceSheetsChangeInDebt(reportDate, warnings)) is decimal netIssuanceOfDebt)
                cashFlowReport.NetIssuanceOfDebt = netIssuanceOfDebt;
            else
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfDebt", warnings);

            if (rawCashFlowReport.HasValue("CashDividendsPaid"))
            {
                cashFlowReport.DividendsPaid = rawCashFlowReport["CashDividendsPaid"]!.Value;
                if (inAdjustmentWindow)
                    totalAnnualDividendsPaid += cashFlowReport.DividendsPaid;
            }

            if (rawCashFlowReport.HasValue("NetCommonStockIssuance"))
            {
                cashFlowReport.NetIssuanceOfStock = rawCashFlowReport["NetCommonStockIssuance"]!.Value;
                if (inAdjustmentWindow)
                    totalAnnualCommonStockIssuance += cashFlowReport.NetIssuanceOfStock;
            }
            else
            {
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfStock", warnings);
            }

            if (rawCashFlowReport.HasValue("NetPreferredStockIssuance"))
            {
                cashFlowReport.NetIssuanceOfPreferredStock = rawCashFlowReport["NetPreferredStockIssuance"]!.Value;
                if (inAdjustmentWindow)
                    totalAnnualPreferredStockIssuance += cashFlowReport.NetIssuanceOfPreferredStock;
            }
            else
            {
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfPreferredStock", warnings);
            }

            if ((rawCashFlowReport["ChangeInWorkingCapital"]
                    ?? CalcConsecutiveBalanceSheetsChangeInWorkingCapital(reportDate, warnings)) is decimal changeInWorkingCapital)
                cashFlowReport.ChangeInWorkingCapital = changeInWorkingCapital;
            else
                LogMissingPropertyWarning(reportDate, "ChangeInWorkingCapital", warnings);

            SetDepreciationAmortizationAndDepletion(reportDate, rawCashFlowReport, cashFlowReport, warnings);
            SetNetPPEPurchaseAndSale(reportDate, rawCashFlowReport, cashFlowReport, warnings);

            if (rawCashFlowReport.HasValue("DeferredTax"))
                cashFlowReport.DeferredTax = rawCashFlowReport["DeferredTax"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "deferredTax", warnings);

            if (rawCashFlowReport.HasValue("OtherNonCashItems"))
                cashFlowReport.OtherNonCashItems = rawCashFlowReport["OtherNonCashItems"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "otherNonCashItems", warnings);

            _annualProcessedCashFlowItems.Add(reportDate, cashFlowReport);
        }

        EstimateMissingRetainedEarnings();

        CurAdjustedRetainedEarnings = CurRetainedEarnings + totalAnnualDividendsPaid - totalAnnualCommonStockIssuance - totalAnnualPreferredStockIssuance;
    }

    /// <summary>
    /// Return on equity over matched annual years (B11, decisions 8 and 24): Σ numerator / Σ average equity, where a
    /// year's average equity is the mean of its opening and closing annual equity. Annual balance sheets only.
    /// ROE (CF) uses net cash flow + dividends in every year; ROE (OE) owner earnings in the years that count for them.
    /// </summary>
    private void CalcReturnOnEquityInputs()
    {
        (RoeCashFlowNumerator, RoeCashFlowEquity) =
            CalcReturnOnEquityInputs(_ => true, item => item.NetCashFlow + item.DividendsPaid);
        (RoeOwnerEarningsNumerator, RoeOwnerEarningsEquity) =
            CalcReturnOnEquityInputs(item => item.IsUsableForOwnerEarnings, item => item.OwnerEarnings);
        RoeOwnerEarningsByYear = [.. MatchedRoeYears(item => item.IsUsableForOwnerEarnings)
            .Select(y => new AnnualReturnOnEquity(y.Date, y.AverageEquity > 0 ? y.Item.OwnerEarnings / y.AverageEquity : null))
            .Reverse()];
    }

    private (decimal? Numerator, decimal? Equity) CalcReturnOnEquityInputs(
        Func<CashFlowItem, bool> isRoeYear, Func<CashFlowItem, decimal> numeratorOf)
    {
        List<KeyValuePair<DateOnly, CashFlowItem>> years = [.. _annualProcessedCashFlowItems.Where(kv => isRoeYear(kv.Value))];
        if (years.Count == 0)
            return (null, null);

        decimal numeratorSum = 0;
        decimal equitySum = 0;
        int matchedYears = 0;
        foreach ((_, CashFlowItem item, decimal averageEquity) in MatchedRoeYears(isRoeYear))
        {
            numeratorSum += numeratorOf(item);
            equitySum += averageEquity;
            matchedYears++;
        }

        if (matchedYears >= 2)
            return (numeratorSum, equitySum);

        // Fewer than 2 usable years: the average over all the ROE's years / the latest annual equity.
        RawReportDataMap? latestWithEquity = _annualRawBalanceSheets.Values.LastOrDefault(bs => bs.HasValue("StockholdersEquity"));
        if (latestWithEquity is null)
            return (null, null);
        return (years.Average(kv => numeratorOf(kv.Value)), latestWithEquity["StockholdersEquity"]);
    }

    /// <summary>
    /// The ROE years, oldest first (B11, decision 24): annual cash flow years that pass <paramref name="isRoeYear"/>
    /// and have closing equity. A year's average equity is the mean of its opening and closing annual equity; the
    /// opening counts only if it is the previous annual balance sheet, about a year earlier, with equity.
    /// </summary>
    private IEnumerable<(DateOnly Date, CashFlowItem Item, decimal AverageEquity)> MatchedRoeYears(Func<CashFlowItem, bool> isRoeYear)
    {
        foreach ((DateOnly date, CashFlowItem item) in _annualProcessedCashFlowItems)
        {
            if (!isRoeYear(item))
                continue;

            // A year without closing equity is skipped.
            if (!_annualRawBalanceSheets.TryGetValue(date, out RawReportDataMap? closingSheet)
                || closingSheet["StockholdersEquity"] is not decimal closing)
            {
                continue;
            }

            KeyValuePair<DateOnly, RawReportDataMap> opening = _annualRawBalanceSheets.LastOrDefault(kv => kv.Key < date);
            int daysBetween = date.DayNumber - opening.Key.DayNumber;
            decimal averageEquity = opening.Value is not null
                && daysBetween is >= MinDaysBetweenAnnualBalanceSheets and <= MaxDaysBetweenAnnualBalanceSheets
                && opening.Value["StockholdersEquity"] is decimal openingEquity
                    ? (openingEquity + closing) / 2
                    : closing;

            yield return (date, item, averageEquity);
        }
    }

    /// <summary>
    /// The data provider leaves RetainedEarnings (with the rest of the equity breakdown) blank on many recent balance
    /// sheets. When the balance sheet a figure comes from lacks it, estimate it from the nearest annual balance sheet
    /// that has it, one annual cash flow year at a time with net income - dividends: forward for the current figure,
    /// backward for the oldest (decision 23). No estimate if a year in between has no net income.
    /// </summary>
    private void EstimateMissingRetainedEarnings()
    {
        if (CurRetainedEarnings is null && _mostRecentReportDate is DateOnly current)
        {
            KeyValuePair<DateOnly, RawReportDataMap> anchor = _annualRawBalanceSheets
                .LastOrDefault(kv => kv.Key <= current && kv.Value.HasValue("RetainedEarnings"));
            if (anchor.Value is not null)
            {
                decimal? estimate = anchor.Value["RetainedEarnings"];
                foreach ((DateOnly date, CashFlowItem item) in _annualProcessedCashFlowItems)
                {
                    if (date > anchor.Key && date <= current)
                        estimate += item.NetIncomeFromContinuingOperations - item.DividendsPaid;
                }
                CurRetainedEarnings = estimate;
                CurRetainedEarningsEstimated = estimate is not null;
            }
        }

        if (OldestRetainedEarnings is null && _oldestReportDate is DateOnly oldest)
        {
            KeyValuePair<DateOnly, RawReportDataMap> anchor = _annualRawBalanceSheets
                .FirstOrDefault(kv => kv.Key >= oldest && kv.Value.HasValue("RetainedEarnings"));
            if (anchor.Value is not null)
            {
                decimal? estimate = anchor.Value["RetainedEarnings"];
                foreach ((DateOnly date, CashFlowItem item) in _annualProcessedCashFlowItems)
                {
                    if (date > oldest && date <= anchor.Key)
                        estimate -= item.NetIncomeFromContinuingOperations - item.DividendsPaid;
                }
                OldestRetainedEarnings = estimate;
                OldestRetainedEarningsEstimated = estimate is not null;
            }
        }
    }

    /// <summary>
    /// Depreciation and amortization, counted once. DepreciationAndAmortization is the combined total, so it is
    /// preferred outright; otherwise Depreciation + Amortization. As a last resort DepreciationAmortizationDepletion
    /// is used, and then Depletion is not added separately, since it is already included.
    /// </summary>
    private static void SetDepreciationAmortizationAndDepletion(
        DateOnly reportDate, RawReportDataMap rawCashFlowReport, CashFlowItem cashFlowReport, IList<string> warnings)
    {
        bool hasDepletion = rawCashFlowReport.HasValue("Depletion");

        if (rawCashFlowReport.HasValue("DepreciationAndAmortization"))
        {
            cashFlowReport.DepreciationAndAmortization = rawCashFlowReport["DepreciationAndAmortization"]!.Value;
        }
        else if (rawCashFlowReport.HasValue("Depreciation") || rawCashFlowReport.HasValue("Amortization"))
        {
            cashFlowReport.DepreciationAndAmortization =
                (rawCashFlowReport["Depreciation"] ?? 0) + (rawCashFlowReport["Amortization"] ?? 0);
        }
        else if (rawCashFlowReport.HasValue("DepreciationAmortizationDepletion"))
        {
            cashFlowReport.DepreciationAndAmortization = rawCashFlowReport["DepreciationAmortizationDepletion"]!.Value;
            return; // Depletion is included
        }
        else
        {
            LogMissingPropertyWarning(reportDate, "depreciation and amortization", warnings);
        }

        // When DepreciationAmortizationDepletion equals the D&A figure, depletion is already inside it (decision 16).
        // When Depletion itself equals the D&A figure, the D&A figure is the depletion (B15, decision 27).
        bool depletionIncludedInDA = rawCashFlowReport["DepreciationAmortizationDepletion"] == cashFlowReport.DepreciationAndAmortization
            || rawCashFlowReport["Depletion"] == cashFlowReport.DepreciationAndAmortization;

        if (hasDepletion && !depletionIncludedInDA)
            cashFlowReport.Depletion = rawCashFlowReport["Depletion"]!.Value;
        else if (!hasDepletion)
            LogMissingPropertyWarning(reportDate, "depletion", warnings);
    }

    /// <summary>
    /// Net PP&amp;E purchases and sales, negative for a net outflow. Uses NetPPEPurchaseAndSale, or
    /// PurchaseOfPPE + SaleOfPPE when that is the larger outflow (NetPPEPurchaseAndSale sometimes misses most of
    /// the purchases; decisions 14 and 21). Falls back to PurchaseOfPPE + SaleOfPPE, then PurchaseOfPPE alone, then
    /// -CapitalExpenditureReported (which is reported as a positive amount).
    /// </summary>
    private static void SetNetPPEPurchaseAndSale(
        DateOnly reportDate, RawReportDataMap rawCashFlowReport, CashFlowItem cashFlowReport, IList<string> warnings)
    {
        cashFlowReport.HasCapExData = true;

        if (rawCashFlowReport.HasValue("NetPPEPurchaseAndSale") && rawCashFlowReport.HasValue("PurchaseOfPPE"))
            cashFlowReport.NetPPEPurchaseAndSale = Math.Min(
                rawCashFlowReport["NetPPEPurchaseAndSale"]!.Value,
                rawCashFlowReport["PurchaseOfPPE"]!.Value + (rawCashFlowReport["SaleOfPPE"] ?? 0));
        else if (rawCashFlowReport.HasValue("NetPPEPurchaseAndSale"))
        {
            // With no PurchaseOfPPE to check it against, a non-negative NetPPEPurchaseAndSale (a net sale) is not
            // trusted: it often misses the purchases. Use CapitalExpenditureReported if present; otherwise the year
            // has no capex data, and is left out of the owner-earnings average if it has D&A (decision 21).
            decimal netPPE = rawCashFlowReport["NetPPEPurchaseAndSale"]!.Value;
            if (netPPE >= 0 && rawCashFlowReport.HasValue("CapitalExpenditureReported"))
            {
                cashFlowReport.NetPPEPurchaseAndSale = -rawCashFlowReport["CapitalExpenditureReported"]!.Value;
            }
            else
            {
                cashFlowReport.NetPPEPurchaseAndSale = netPPE;
                cashFlowReport.HasCapExData = netPPE < 0;
            }
        }
        else if (rawCashFlowReport.HasValue("PurchaseOfPPE") && rawCashFlowReport.HasValue("SaleOfPPE"))
            cashFlowReport.NetPPEPurchaseAndSale = rawCashFlowReport["PurchaseOfPPE"]!.Value + rawCashFlowReport["SaleOfPPE"]!.Value;
        else if (rawCashFlowReport.HasValue("PurchaseOfPPE"))
            cashFlowReport.NetPPEPurchaseAndSale = rawCashFlowReport["PurchaseOfPPE"]!.Value;
        else if (rawCashFlowReport.HasValue("CapitalExpenditureReported"))
            cashFlowReport.NetPPEPurchaseAndSale = -rawCashFlowReport["CapitalExpenditureReported"]!.Value;
        else
        {
            cashFlowReport.HasCapExData = false;
            LogMissingPropertyWarning(reportDate, "NetPPEPurchaseAndSale", warnings);
        }
    }

    /// <summary>
    /// Get the difference in debt between balance sheets for the given report date and the one balance sheet before it.
    /// </summary>
    private decimal? CalcConsecutiveBalanceSheetsChangeInDebt(DateOnly reportDate, IList<string> warnings)
    {
        (RawReportDataMap? prevReport, RawReportDataMap? thisReport) = GetThisAndPrevBalanceSheets(reportDate);

        if (prevReport is null || thisReport is null)
        {
            return null;
        }

        decimal? prevDebt = FirstPresentOrWarn(prevReport, "Balance sheet", reportDate, warnings, LongTermDebtKeys);
        decimal? curDebt = FirstPresentOrWarn(thisReport, "Balance sheet", reportDate, warnings, LongTermDebtKeys);

        decimal? debtDiff = null;
        if (prevDebt is not null && curDebt is not null)
            debtDiff = curDebt - prevDebt;

        return debtDiff;
    }

    /// <summary>
    /// Get the difference in working capital between balance sheets for the given report date and the one balance sheet before it.
    /// </summary>
    private decimal? CalcConsecutiveBalanceSheetsChangeInWorkingCapital(DateOnly reportDate, IList<string> warnings)
    {
        (RawReportDataMap? prevReport, RawReportDataMap? thisReport) = GetThisAndPrevBalanceSheets(reportDate);

        if (prevReport is null || thisReport is null)
            return null;

        decimal? prevWorkingCapital = CalcWorkingCapitalFromRawBalanceSheet(reportDate, prevReport, warnings);
        decimal? curWorkingCapital = CalcWorkingCapitalFromRawBalanceSheet(reportDate, thisReport, warnings);

        // Cash-flow convention: an increase in working capital consumes cash, so it is negative.
        decimal? changeInWorkingCapital = null;
        if (prevWorkingCapital is not null && curWorkingCapital is not null)
            changeInWorkingCapital = prevWorkingCapital - curWorkingCapital;

        return changeInWorkingCapital;
    }

    private decimal? CalcIncomeStatement_NetIncomeFromContinuingOperations(DateOnly reportDate, IList<string> warnings)
    {
        var rpt = GetMatchingIncomeStatementByReportDate(reportDate);
        if (rpt is null)
            return null;

        return FirstPresentOrWarn(rpt, "Income statement", reportDate, warnings, "NetIncomeContinuousOperations");
    }

    private (RawReportDataMap? prevReport, RawReportDataMap? thisReport) GetThisAndPrevBalanceSheets(DateOnly reportDate)
    {
        RawReportDataMap? prevReport = null;
        RawReportDataMap? thisReport = null;

        foreach ((DateOnly d, RawReportDataMap rpt) in _annualRawBalanceSheets)
        {
            if (d < reportDate)
                prevReport = rpt;
            else if (d == reportDate)
                thisReport = rpt;
        }

        return (prevReport, thisReport);
    }

    private RawReportDataMap? GetMatchingIncomeStatementByReportDate(DateOnly reportDate)
    {
        _ = _annualRawIncomeStatements.TryGetValue(reportDate, out RawReportDataMap? value);
        return value;
    }

    /// <summary>
    /// The value of the first of <paramref name="keys"/> in the statement; null, with a warning naming the keys, when
    /// none is there. The report date is the cash flow year being processed, also for the previous balance sheet.
    /// </summary>
    private static decimal? FirstPresentOrWarn(
        RawReportDataMap statement, string statementName, DateOnly reportDate, IList<string> warnings, params ReadOnlySpan<string> keys)
    {
        decimal? value = statement.FirstPresent(keys);
        if (value is null)
        {
            var quotedKeys = new List<string>(keys.Length);
            foreach (string key in keys)
                quotedKeys.Add($"'{key}'");
            warnings.Add($"{statementName}(report date: {reportDate}) missing property {string.Join(" and ", quotedKeys)}");
        }
        return value;
    }

    /// <summary>
    /// Operating working capital: current assets excluding cash, minus current liabilities excluding current debt.
    /// A missing cash or current-debt key counts as 0.
    /// </summary>
    private static decimal? CalcWorkingCapitalFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        decimal? currentAssets = FirstPresentOrWarn(rawBalanceSheet, "Balance sheet", reportDate, warnings, "CurrentAssets");
        decimal? currentLiabilities = FirstPresentOrWarn(rawBalanceSheet, "Balance sheet", reportDate, warnings, "CurrentLiabilities");

        if (currentAssets is null || currentLiabilities is null)
            return null;

        decimal cash = rawBalanceSheet.FirstPresent("CashCashEquivalentsAndShortTermInvestments", "CashAndCashEquivalents") ?? 0;
        decimal currentDebt = rawBalanceSheet.FirstPresent("CurrentDebtAndCapitalLeaseObligation", "CurrentDebt") ?? 0;

        return (currentAssets - cash) - (currentLiabilities - currentDebt);
    }

    private static void LogMissingPropertyWarning(DateOnly reportDate, string missingPropertyName, IList<string> warnings)
    {
        warnings.Add($"Missing property '{missingPropertyName}'. Report date: {reportDate}");
    }

    #endregion
}
