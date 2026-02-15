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
    public decimal CurTotalShareholdersEquity { get; set; }
    public decimal CurGoodwill { get; set; }
    public decimal CurIntangibles { get; set; }
    public decimal CurLongTermDebt { get; set; }
    public decimal CurDividendsPaid { get; set; }
    public decimal CurRetainedEarnings { get; set; }
    public decimal CurAdjustedRetainedEarnings { get; set; }
    public decimal OldestRetainedEarnings { get; set; }
    public bool DidAdjustedRetainedEarningsIncrease => CurAdjustedRetainedEarnings > OldestRetainedEarnings;
    public decimal CurBookValue => CurTotalShareholdersEquity - (CurGoodwill + CurIntangibles);
    public decimal LongTermDebtToBookRatio => Utilities.DivSafe(CurLongTermDebt, CurBookValue);
    public decimal AverageNetCashFlow
    {
        get
        {
            decimal totalGrossCashFlow = _annualProcessedCashFlowItems.Sum(cashFlowItem => cashFlowItem.Value.NetCashFlow);
            return Utilities.DivSafe(totalGrossCashFlow, _annualProcessedCashFlowItems.Count, decimal.MinValue);
        }
    }
    public decimal AverageOwnerEarnings
    {
        get
        {
            decimal totalGrossOwnerEarnings = _annualProcessedCashFlowItems.Sum(cashFlowItem => cashFlowItem.Value.OwnerEarnings);
            return Utilities.DivSafe(totalGrossOwnerEarnings, _annualProcessedCashFlowItems.Count, decimal.MinValue);
        }
    }
    public decimal EstimatedNextYearBookValue_FromCashFlow
    {
        get
        {
            return CurBookValue == decimal.MinValue || AverageNetCashFlow == decimal.MinValue
                ? decimal.MinValue
                : CurBookValue + AverageNetCashFlow;
        }
    }
    public decimal EstimatedNextYearBookValue_FromOwnerEarnings
    {
        get
        {
            return CurBookValue == decimal.MinValue || AverageOwnerEarnings == decimal.MinValue
                ? decimal.MinValue
                : CurBookValue + AverageOwnerEarnings;
        }
    }
    public decimal MaxPrice
    {
        get
        {
            if (CurNumShares <= 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue)
            {
                return -1;
            }

            // DoesPassCheck_PriceToBookSmallEnough
            // max price = 3 * this.CurBookValue
            decimal maxPriceSoFar = 3M * CurBookValue;

            // DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough
            // max price = 20 * (EstimatedNextYearBookValue_FromCashFlow - curDividendsPaid - CurBookValue)
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * (EstimatedNextYearBookValue_FromCashFlow - CurDividendsPaid - CurBookValue));

            // DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough
            // max price = 20 * (EstimatedNextYearBookValue_FromOwnerEarnings - curDividendsPaid - CurBookValue)
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * (EstimatedNextYearBookValue_FromOwnerEarnings - CurDividendsPaid - CurBookValue));

            return maxPriceSoFar / CurNumShares;
        }
    }
    public decimal DebtToEquityRatio => Utilities.DivSafe(CurLongTermDebt, CurTotalShareholdersEquity);
    public int NumAnnualProcessedCashFlowReports => _annualProcessedCashFlowItems.Count;
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
        TransformRawFinancialReports(warnings);
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

        if (oldestBalanceSheet.HasValue("RetainedEarnings"))
            OldestRetainedEarnings = oldestBalanceSheet["RetainedEarnings"]!.Value;
        else
            LogMissingPropertyWarning(oldestReportDate, "RetainedEarnings", warnings);
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

        if (mostRecentCashFlowStatement.HasValue("CashDividendsPaid"))
            CurDividendsPaid = mostRecentCashFlowStatement["CashDividendsPaid"]!.Value;

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
                totalAnnualDividendsPaid += rawCashFlowReport["CashDividendsPaid"]!.Value;

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

            if (rawCashFlowReport.HasValue("Depreciation"))
                cashFlowReport.Depreciation = rawCashFlowReport["Depreciation"]!.Value;
            else if (rawCashFlowReport.HasValue("DepreciationAndAmortization"))
                cashFlowReport.Depreciation = rawCashFlowReport["DepreciationAndAmortization"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "depreciation", warnings);

            if (rawCashFlowReport.HasValue("Depletion"))
                cashFlowReport.Depletion = rawCashFlowReport["Depletion"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "depletion", warnings);

            if (rawCashFlowReport.HasValue("Amortization"))
                cashFlowReport.Amortization = rawCashFlowReport["Amortization"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "amortization", warnings);

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

        CurAdjustedRetainedEarnings = CurRetainedEarnings + totalAnnualDividendsPaid - totalAnnualCommonStockIssuance - totalAnnualPreferredStockIssuance;
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
        if (prevDebt is not null && curDebt is null)
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

        decimal? changeInWorkingCapital = null;
        if (prevWorkingCapital is not null && curWorkingCapital is not null)
            changeInWorkingCapital = curWorkingCapital - prevWorkingCapital;

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

    private static decimal? CalcWorkingCapitalFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet, IList<string> warnings)
    {
        decimal? currentAssets = CalcCurrentAssetsFromRawBalanceSheet(reportDate, rawBalanceSheet, warnings);
        decimal? currentLiabilities = CalcCurrentLiabilitiesFromRawBalanceSheet(reportDate, rawBalanceSheet, warnings);

        return currentAssets is not null && currentLiabilities is not null
            ? currentAssets - currentLiabilities
            : null;
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
