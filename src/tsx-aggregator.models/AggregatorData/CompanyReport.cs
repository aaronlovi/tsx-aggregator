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

    [JsonIgnore]
    public bool NeedGetAnnualIncomes =>
        _annualProcessedCashFlowItems.Values.Any(cashFlowItem => cashFlowItem.NetIncomeFromContinuingOperations is null);
    public long CurNumShares { get; init; }
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

    public void AddBalanceSheet(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt)
    {
        switch (rptPeriodType)
        {
            case ReportPeriodTypes.Annual:
                AddAnnualBalanceSheet(reportDate, rpt);
                break;
            case ReportPeriodTypes.Quarterly:
            case ReportPeriodTypes.SemiAnnual: // Treat the same as quarterly
                AddNonAnnualBalanceSheet(reportDate, rpt);
                break;
            case ReportPeriodTypes.Undefined:
            default:
                break;
        }
    }

    public void AddCashFlowStatement(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt)
    {
        switch (rptPeriodType)
        {
            case ReportPeriodTypes.Annual:
                AddAnnualCashFlowStatement(reportDate, rpt);
                break;
            case ReportPeriodTypes.Quarterly:
            case ReportPeriodTypes.SemiAnnual: // Treat the same as quarterly
                AddNonAnnualCashFlowStatement(reportDate, rpt);
                break;
            case ReportPeriodTypes.Undefined:
            default:
                break;
        }
    }

    public void AddIncomeStatement(ReportPeriodTypes rptPeriodType, DateOnly reportDate, RawReportDataMap rpt)
    {
        switch (rptPeriodType)
        {
            case ReportPeriodTypes.Annual:
                AddAnnualIncomeStatement(reportDate, rpt);
                break;
            case ReportPeriodTypes.Quarterly:
            case ReportPeriodTypes.SemiAnnual: // Treat the same as quarterly
                AddNonAnnualIncomeStatement(reportDate, rpt);
                break;
            case ReportPeriodTypes.Undefined:
            default:
                break;
        }
    }

    public void AddAnnualIncomeStatement(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_annualRawIncomeStatements.ContainsKey(reportDate))
            return;
        _annualRawIncomeStatements.Add(reportDate, rpt);
    }

    public void AddAnnualBalanceSheet(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_annualRawBalanceSheets.ContainsKey(reportDate))
            return;
        _annualRawBalanceSheets.Add(reportDate, rpt);
    }

    public void AddAnnualCashFlowStatement(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_annualRawCashFlowReports.ContainsKey(reportDate))
            return;
        _annualRawCashFlowReports.Add(reportDate, rpt);
    }

    public void AddNonAnnualIncomeStatement(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_nonAnnualRawIncomeStatements.ContainsKey(reportDate))
            return;
        _nonAnnualRawIncomeStatements.Add(reportDate, rpt);
    }

    public void AddNonAnnualBalanceSheet(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_nonAnnualRawBalanceSheets.ContainsKey(reportDate))
            return;
        _nonAnnualRawBalanceSheets.Add(reportDate, rpt);
    }

    public void AddNonAnnualCashFlowStatement(DateOnly reportDate, RawReportDataMap rpt)
    {
        if (_nonAnnualRawCashFlowReports.ContainsKey(reportDate))
            return;
        _nonAnnualRawCashFlowReports.Add(reportDate, rpt);
    }

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
        var oldestReportDate = DateOnly.MaxValue;
        RawReportDataMap? oldestBalanceSheet = null;
        RawReportDataMap? oldestCashFlowStatement = null;
        RawReportDataMap? oldestIncomeStatement = null;

        foreach ((DateOnly reportDate, RawReportDataMap rawBalanceSheet) in _nonAnnualRawBalanceSheets)
        {
            bool foundMatchingCashFlow = _nonAnnualRawCashFlowReports.ContainsKey(reportDate);
            bool foundMatchingIncome = _nonAnnualRawIncomeStatements.ContainsKey(reportDate);
            // All statement types must contain the given report date
            if (reportDate > oldestReportDate
                || !foundMatchingCashFlow
                || !foundMatchingIncome)
            {
                if (!foundMatchingCashFlow)
                    warnings.Add($"Non-annual balance sheet for {reportDate} but no matching cash flow statement");
                if (!foundMatchingIncome)
                    warnings.Add($"Non-annual balance sheet for {reportDate} but no matching income statement");
                continue;
            }

            oldestReportDate = reportDate;
            oldestBalanceSheet = rawBalanceSheet;
            oldestCashFlowStatement = _nonAnnualRawCashFlowReports[reportDate];
            oldestIncomeStatement = _nonAnnualRawIncomeStatements[reportDate];
        }

        // Maybe one of the annual reports is the oldest?
        foreach ((DateOnly reportDate, RawReportDataMap rawBalanceSheet) in _annualRawBalanceSheets)
        {
            bool foundMatchingCashFlow = _annualRawCashFlowReports.ContainsKey(reportDate);
            bool foundMatchingIncome = _annualRawIncomeStatements.ContainsKey(reportDate);
            // All statement types must contain the given report date
            if (reportDate > oldestReportDate
                || !_annualRawCashFlowReports.ContainsKey(reportDate)
                || !_annualRawIncomeStatements.ContainsKey(reportDate))
            {
                if (!foundMatchingCashFlow)
                    warnings.Add($"Annual balance sheet for {reportDate} but no matching cash flow statement");
                if (!foundMatchingIncome)
                    warnings.Add($"Annual balance sheet for {reportDate} but no matching income statement");
                continue;
            }

            oldestReportDate = reportDate;
            oldestBalanceSheet = rawBalanceSheet;
            oldestCashFlowStatement = _annualRawCashFlowReports[reportDate];
            oldestIncomeStatement = _annualRawIncomeStatements[reportDate];
        }

        if (oldestReportDate == DateOnly.MaxValue
            || oldestBalanceSheet is null
            || oldestCashFlowStatement is null
            || oldestIncomeStatement is null)
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
        var mostRecentReportDate = DateOnly.MinValue;
        RawReportDataMap? mostRecentBalanceSheet = null;
        RawReportDataMap? mostRecentCashFlowStatement = null;
        RawReportDataMap? mostRecentIncomeStatement = null;

        foreach ((DateOnly reportDate, RawReportDataMap rawBalanceSheet) in _nonAnnualRawBalanceSheets)
        {
            // All statement types must contain the given report date
            if (reportDate < mostRecentReportDate
                || !_nonAnnualRawCashFlowReports.ContainsKey(reportDate)
                || !_nonAnnualRawIncomeStatements.ContainsKey(reportDate))
            {
                continue;
            }

            mostRecentReportDate = reportDate;
            mostRecentBalanceSheet = rawBalanceSheet;
            mostRecentCashFlowStatement = _nonAnnualRawCashFlowReports[reportDate];
            mostRecentIncomeStatement = _nonAnnualRawIncomeStatements[reportDate];
        }

        // Maybe one of the annual reports is the most recent?
        foreach ((DateOnly reportDate, RawReportDataMap rawBalanceSheet) in _annualRawBalanceSheets)
        {
            // All statement types must contain the given report date
            if (reportDate < mostRecentReportDate
                || !_annualRawCashFlowReports.ContainsKey(reportDate)
                || !_annualRawIncomeStatements.ContainsKey(reportDate))
            {
                continue;
            }

            mostRecentReportDate = reportDate;
            mostRecentBalanceSheet = rawBalanceSheet;
            mostRecentCashFlowStatement = _annualRawCashFlowReports[reportDate];
            mostRecentIncomeStatement = _annualRawIncomeStatements[reportDate];
        }

        if (mostRecentReportDate == DateOnly.MinValue
            || mostRecentBalanceSheet is null
            || mostRecentCashFlowStatement is null
            || mostRecentIncomeStatement is null)
        {
            return;
        }

        _mostRecentReportDate = mostRecentReportDate;

        if (mostRecentBalanceSheet.HasValue("StockholdersEquity"))
            CurTotalShareholdersEquity = mostRecentBalanceSheet["StockholdersEquity"]!.Value;
        else
            warnings.Add("TransformMostRecentFinancialReports: missing property current StockholdersEquity");

        if (mostRecentBalanceSheet.HasValue("Goodwill"))
            CurGoodwill = mostRecentBalanceSheet["Goodwill"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets") && mostRecentBalanceSheet.HasValue("OtherIntangibleAssets"))
            CurGoodwill = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value - mostRecentBalanceSheet["OtherIntangibleAssets"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets"))
            CurGoodwill = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value;
        else
            warnings.Add("TransformMostRecentFinancialReports: missing property current Goodwill");

        if (mostRecentBalanceSheet.HasValue("OtherIntangibleAssets"))
            CurIntangibles = mostRecentBalanceSheet["OtherIntangibleAssets"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets") && mostRecentBalanceSheet.HasValue("Goodwill"))
            CurIntangibles = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value - mostRecentBalanceSheet["Goodwill"]!.Value;
        else
            warnings.Add("parseQuarterlyFigures: missing property current Intangibles");

        if (mostRecentBalanceSheet.HasValue("LongTermDebtAndCapitalLeaseObligation"))
            CurLongTermDebt = mostRecentBalanceSheet["LongTermDebtAndCapitalLeaseObligation"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("LongTermDebt"))
            CurLongTermDebt = mostRecentBalanceSheet["LongTermDebt"]!.Value;
        else
            warnings.Add("parseQuarterlyFigures: missing property current LongTermDebt");

        if (mostRecentBalanceSheet.HasValue("RetainedEarnings"))
            CurRetainedEarnings = mostRecentBalanceSheet["RetainedEarnings"]!.Value;
        else
            warnings.Add("parseQuarterlyFigures: missing property current RetainedEarnings");
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

            if (rawCashFlowReport.HasValue("NetIncomeFromContinuingOperations"))
            {
                cashFlowReport.NetIncomeFromContinuingOperations = rawCashFlowReport["NetIncomeFromContinuingOperations"]!.Value;
            }
            else
            {
                decimal? netIncome = CalcIncomeStatement_NetIncomeFromContinuingOperations(reportDate, warnings);
                if (netIncome is not null)
                    cashFlowReport.NetIncomeFromContinuingOperations = netIncome;
                else
                    LogMissingPropertyWarning(reportDate, "NetIncomeFromContinuingOperations", warnings);
            }

            if (rawCashFlowReport.HasValue("ChangesInCash"))
                cashFlowReport.GrossCashFlow = rawCashFlowReport["ChangesInCash"]!.Value;
            else if (rawCashFlowReport.HasValue("BeginningCashPosition") && rawCashFlowReport.HasValue("EndCashPosition"))
                cashFlowReport.GrossCashFlow = rawCashFlowReport["EndCashPosition"]!.Value - rawCashFlowReport["BeginningCashPosition"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "GrossCashFlow", warnings);

            if (rawCashFlowReport.HasValue("NetIssuancePaymentsOfDebt"))
                cashFlowReport.NetIssuanceOfDebt = rawCashFlowReport["NetIssuancePaymentsOfDebt"]!.Value;
            else if (rawCashFlowReport.HasValue("NetLongTermDebtIssuance"))
                cashFlowReport.NetIssuanceOfDebt = rawCashFlowReport["NetLongTermDebtIssuance"]!.Value;
            else
            {
                decimal? debtDiff = CalcConsecutiveBalanceSheetsChangeInDebt(reportDate, warnings);
                if (debtDiff is not null)
                    cashFlowReport.NetIssuanceOfDebt = debtDiff!.Value;
                else
                    LogMissingPropertyWarning(reportDate, "NetIssuanceOfDebt", warnings);
            }

            if (rawCashFlowReport.HasValue("CashDividendsPaid"))
            {
                cashFlowReport.DividendsPaid = rawCashFlowReport["CashDividendsPaid"]!.Value;
                totalAnnualDividendsPaid += cashFlowReport.DividendsPaid;
            }

            if (rawCashFlowReport.HasValue("NetCommonStockIssuance"))
            {
                cashFlowReport.NetIssuanceOfStock = rawCashFlowReport["NetCommonStockIssuance"]!.Value;
                totalAnnualCommonStockIssuance += cashFlowReport.NetIssuanceOfStock;
            }
            else
            {
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfStock", warnings);
            }

            if (rawCashFlowReport.HasValue("NetPreferredStockIssuance"))
            {
                cashFlowReport.NetIssuanceOfPreferredStock = rawCashFlowReport["NetPreferredStockIssuance"]!.Value;
                totalAnnualPreferredStockIssuance += cashFlowReport.NetIssuanceOfPreferredStock;
            }
            else
            {
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfPreferredStock", warnings);
            }

            if (rawCashFlowReport.HasValue("ChangeInWorkingCapital"))
                cashFlowReport.ChangeInWorkingCapital = rawCashFlowReport["ChangeInWorkingCapital"]!.Value;
            else
            {
                decimal? changeInWorkingCapital = CalcConsecutiveBalanceSheetsChangeInWorkingCapital(reportDate, warnings);
                if (changeInWorkingCapital is not null)
                    cashFlowReport.ChangeInWorkingCapital = changeInWorkingCapital.Value;
                else
                    LogMissingPropertyWarning(reportDate, "ChangeInWorkingCapital", warnings);
            }

            SetDepreciationAmortizationAndDepletion(reportDate, rawCashFlowReport, cashFlowReport, warnings);
            SetNetPPEPurchaseAndSale(reportDate, rawCashFlowReport, cashFlowReport, warnings);

            if (rawCashFlowReport.HasValue("DeferredTax"))
                cashFlowReport.DeferredTax = rawCashFlowReport["DeferredTax"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "defferedTax", warnings);

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

        decimal? prevDebt = CalcLongTermDebtFromRawBalanceSheet(reportDate, prevReport, warnings);
        decimal? curDebt = CalcLongTermDebtFromRawBalanceSheet(reportDate, thisReport, warnings);

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

        return CalcNetIncomeFromContinuingOperationsFromRawIncomeStatement(reportDate, rpt, warnings);
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

    private static decimal? CalcLongTermDebtFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        if (rawBalanceSheet.HasValue("LongTermDebtAndCapitalLeaseObligation"))
            return rawBalanceSheet["LongTermDebtAndCapitalLeaseObligation"]!.Value;
        else if (rawBalanceSheet.HasValue("LongTermDebt"))
            return rawBalanceSheet["LongTermDebt"]!.Value;
        else
        {
            warnings.Add($"Balance sheet(report date: {reportDate}) missing property 'LongTermDebtAndCapitalLeaseObligation' and 'LongTermDebt'");
            return null;
        }
    }

    /// <summary>
    /// Operating working capital: current assets excluding cash, minus current liabilities excluding current debt.
    /// A missing cash or current-debt key counts as 0.
    /// </summary>
    private static decimal? CalcWorkingCapitalFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        decimal? currentAssets = CalcCurrentAssetsFromRawBalanceSheet(reportDate, rawBalanceSheet, warnings);
        decimal? currentLiabilities = CalcCurrentLiabilitiesFromRawBalanceSheet(reportDate, rawBalanceSheet, warnings);

        if (currentAssets is null || currentLiabilities is null)
            return null;

        decimal cash = rawBalanceSheet["CashCashEquivalentsAndShortTermInvestments"]
            ?? rawBalanceSheet["CashAndCashEquivalents"]
            ?? 0;
        decimal currentDebt = rawBalanceSheet["CurrentDebtAndCapitalLeaseObligation"]
            ?? rawBalanceSheet["CurrentDebt"]
            ?? 0;

        return (currentAssets - cash) - (currentLiabilities - currentDebt);
    }

    private static decimal? CalcCurrentAssetsFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        if (rawBalanceSheet.HasValue("CurrentAssets"))
        {
            return rawBalanceSheet["CurrentAssets"]!.Value;
        }
        else
        {
            warnings.Add($"Balance sheet(report date: ${reportDate}) missing property 'CurrentAssets'");
            return null;
        }
    }

    private static decimal? CalcCurrentLiabilitiesFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        if (rawBalanceSheet.HasValue("CurrentLiabilities"))
        {
            return rawBalanceSheet["CurrentLiabilities"]!.Value;
        }
        else
        {
            warnings.Add($"Balance sheet(report date: ${reportDate}) missing property 'CurrentLiabilities'");
            return null;
        }
    }

    private static decimal? CalcNetIncomeFromContinuingOperationsFromRawIncomeStatement(DateOnly reportDate, RawReportDataMap rawIncomeStatement, IList<string> warnings)
    {
        if (rawIncomeStatement.HasValue("NetIncomeContinuousOperations"))
        {
            return rawIncomeStatement["NetIncomeContinuousOperations"]!.Value;
        }
        else
        {
            warnings.Add($"Income statement(report date: ${reportDate}) missing property 'NetIncomeContinuousOperations'");
            return null;
        }
    }

    private static void LogMissingPropertyWarning(DateOnly reportDate, string missingPropertyName, IList<string> warnings)
    {
        warnings.Add($"Missing property '{missingPropertyName}'. Report date: {reportDate}");
    }

    #endregion
}
