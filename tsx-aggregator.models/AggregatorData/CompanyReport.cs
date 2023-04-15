using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
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

    public CompanyReport(string symbol, string name, string exchange, decimal pricePerShare, long curNumShares)
    {
        Symbol = symbol;
        Name = name;
        Exchange = exchange;
        PricePerShare = pricePerShare;
        CurNumShares = curNumShares;
        _annualRawCashFlowReports = new();
        _annualRawIncomeStatements = new();
        _annualRawBalanceSheets = new();
        _nonAnnualRawCashFlowReports = new();
        _nonAnnualRawIncomeStatements = new();
        _nonAnnualRawBalanceSheets = new();
        _annualProcessedCashFlowItems = new();
    }

    #region PUBLIC PROPERTIES

    [JsonIgnore]
    public bool NeedGetAnnualIncomes =>
        _annualProcessedCashFlowItems.Values.Any(cashFlowItem => cashFlowItem.NetIncomeFromContinuingOperations is null);
    public decimal PricePerShare { get; init; }
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
    public decimal OldestRetainedEarnings { get; set; }
    public bool DidRetainedEarningsIncrease => CurRetainedEarnings > OldestRetainedEarnings;
    public decimal CurMarketCap => CurNumShares * PricePerShare;
    public decimal CurBookValue => CurTotalShareholdersEquity - (CurGoodwill + CurIntangibles);
    public decimal CurPriceToBookRatio => Utilities.DivSafe(CurMarketCap, CurBookValue);
    public decimal CurPriceToCashFlowRatio => Utilities.DivSafe(CurMarketCap, AverageNetCashFlow);
    public decimal CurPriceToOwnerEarningsRatio => Utilities.DivSafe(CurMarketCap, AverageOwnerEarnings);
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
    public decimal EstimatedNextYearPricePerShare_FromCashFlow
    {
        get
        {
            if (CurNumShares == 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || CurPriceToBookRatio == decimal.MaxValue)
            {
                return decimal.MinValue;
            }

            return EstimatedNextYearBookValue_FromCashFlow * CurPriceToBookRatio / CurNumShares;
        }
    }
    public decimal EstimatedNextYearPricePerShare_FromOwnerEarnings
    {
        get
        {
            if (CurNumShares == 0
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurPriceToBookRatio == decimal.MaxValue)
            {
                return decimal.MinValue;
            }

            return EstimatedNextYearBookValue_FromOwnerEarnings * CurPriceToBookRatio / CurNumShares;
        }
    }
    public decimal EstimatedNextYearReturn_FromCashFlowPercentage
    {
        get
        {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || CurBookValue == decimal.MinValue)
            {
                return decimal.MinValue;
            }

            return 100M * (EstimatedNextYearBookValue_FromCashFlow - CurBookValue) / CurMarketCap;
        }
    }
    public decimal EstimatedNextYearReturn_FromOwnerEarningsPercentage
    {
        get
        {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue)
            {
                return decimal.MinValue;
            }

            return 100M * (EstimatedNextYearBookValue_FromOwnerEarnings - CurBookValue) / CurMarketCap;
        }
    }
    public decimal EstimatedNextYearTotalReturnPercentage_FromCashFlow
    {
        get
        {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || CurBookValue == decimal.MinValue)
            {
                return decimal.MinValue;
            }

            return 100M * (EstimatedNextYearBookValue_FromCashFlow - CurDividendsPaid - CurBookValue) / CurMarketCap;
        }
    }
    public decimal EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings
    {
        get
        {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue)
            {
                return decimal.MinValue;
            }

            return 100M * (EstimatedNextYearBookValue_FromOwnerEarnings - CurDividendsPaid - CurBookValue) / CurMarketCap;
        }
    }
    public decimal MaxPrice
    {
        get
        {
            if (CurNumShares <= 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue
                || CurMarketCap <= 0)
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
    public bool DoesPassCheck_DebtToEquitySmallEnough => DebtToEquityRatio < 0.5M;
    public bool DoesPassCheck_BookValueBigEnough => CurBookValue > 150_000_000M;
    public bool DoesPassCheck_PriceToBookSmallEnough => CurPriceToBookRatio <= 3M;
    public bool DoesPassCheck_AvgCashFlow_Positive => AverageNetCashFlow > 0;
    public bool DoesPassCheck_AvgOwnerEarningsPositive => AverageOwnerEarnings > 0;
    public bool DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough => EstimatedNextYearTotalReturnPercentage_FromCashFlow > 5M;
    public bool DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough => EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings > 5M;
    public bool DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig => EstimatedNextYearTotalReturnPercentage_FromCashFlow < 40M;
    public bool DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig => EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings < 40M;
    public bool DoesPassCheck_DebtToBookRatioSmallEnough => LongTermDebtToBookRatio < 1M;
    public bool DoesPassCheck_RetainedEarningsPositive => CurRetainedEarnings > 0;
    public bool DoesPassCheck_IsHistoryLongEnough => _annualProcessedCashFlowItems.Count >= 4;
    public bool DoesPassCheck_Overall =>
        DoesPassCheck_DebtToEquitySmallEnough
        && DoesPassCheck_BookValueBigEnough
        && DoesPassCheck_PriceToBookSmallEnough
        && DoesPassCheck_AvgCashFlow_Positive
        && DoesPassCheck_AvgOwnerEarningsPositive
        && DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough
        && DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough
        && DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig
        && DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig
        && DoesPassCheck_DebtToBookRatioSmallEnough
        && DoesPassCheck_RetainedEarningsPositive
        && DoesPassCheck_IsHistoryLongEnough
        && DidRetainedEarningsIncrease;
    public int OverallScore =>
        (DoesPassCheck_DebtToEquitySmallEnough ? 1 : 0)
        + (DoesPassCheck_BookValueBigEnough ? 1 : 0)
        + (DoesPassCheck_PriceToBookSmallEnough ? 1 : 0)
        + (DoesPassCheck_AvgCashFlow_Positive ? 1 : 0)
        + (DoesPassCheck_AvgOwnerEarningsPositive ? 1 : 0)
        + (DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough ? 1 : 0)
        + (DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough ? 1 : 0)
        + (DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig ? 1 : 0)
        + (DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig ? 1 : 0)
        + (DoesPassCheck_DebtToBookRatioSmallEnough ? 1 : 0)
        + (DoesPassCheck_RetainedEarningsPositive ? 1 : 0)
        + (DoesPassCheck_IsHistoryLongEnough ? 1 : 0)
        + (DidRetainedEarningsIncrease ? 1 : 0);
    public string ToShortString => $"{Symbol}/{Name}";

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

    public void ProcessReports()
    {
        TransformOldestFinancialReports();
        TransformMostRecentFinancialReports();
        TransformRawFinancialReports();
    }

    #endregion

    #region PRIVATE HELPER METHODS

    private void TransformOldestFinancialReports()
    {
        var oldestReportDate = DateOnly.MaxValue;
        RawReportDataMap? oldestBalanceSheet = null;
        RawReportDataMap? oldestCashFlowStatement = null;
        RawReportDataMap? oldestIncomeStatement = null;

        foreach ((DateOnly reportDate, RawReportDataMap rawBalanceSheet) in _nonAnnualRawBalanceSheets)
        {
            // All statement types must contain the given report date
            if (reportDate > oldestReportDate
                || !_nonAnnualRawCashFlowReports.ContainsKey(reportDate)
                || !_nonAnnualRawIncomeStatements.ContainsKey(reportDate))
            {
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
            // All statement types must contain the given report date
            if (reportDate > oldestReportDate
                || !_annualRawCashFlowReports.ContainsKey(reportDate)
                || !_annualRawIncomeStatements.ContainsKey(reportDate))
            {
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

    }

    private void TransformMostRecentFinancialReports()
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
        //else
        //    _logger.LogWarning("TransformMostRecentFinancialReports: missing property current StockholdersEquity");

        if (mostRecentBalanceSheet.HasValue("Goodwill"))
            CurGoodwill = mostRecentBalanceSheet["Goodwill"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets") && mostRecentBalanceSheet.HasValue("OtherIntangibleAssets"))
            CurGoodwill = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value - mostRecentBalanceSheet["OtherIntangibleAssets"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets"))
            CurGoodwill = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value;
        //else
        //    _logger.LogWarning("TransformMostRecentFinancialReports: missing property current Goodwill");

        if (mostRecentBalanceSheet.HasValue("OtherIntangibleAssets"))
            CurIntangibles = mostRecentBalanceSheet["OtherIntangibleAssets"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("GoodwillAndOtherIntangibleAssets") && mostRecentBalanceSheet.HasValue("Goodwill"))
            CurIntangibles = mostRecentBalanceSheet["GoodwillAndOtherIntangibleAssets"]!.Value - mostRecentBalanceSheet["Goodwill"]!.Value;
        //else
        //    _logger.LogWarning("parseQuarterlyFigures: missing property current Intangibles");

        if (mostRecentBalanceSheet.HasValue("LongTermDebtAndCapitalLeaseObligation"))
            CurLongTermDebt = mostRecentBalanceSheet["LongTermDebtAndCapitalLeaseObligation"]!.Value;
        else if (mostRecentBalanceSheet.HasValue("LongTermDebt"))
            CurLongTermDebt = mostRecentBalanceSheet["LongTermDebt"]!.Value;
        //else
        //    _logger.LogWarning("parseQuarterlyFigures: missing property current LongTermDebt");

        if (mostRecentBalanceSheet.HasValue("RetainedEarnings"))
            CurRetainedEarnings = mostRecentBalanceSheet["RetainedEarnings"]!.Value;
        //else
        //    _logger.LogWarning("parseQuarterlyFigures: missing property current RetainedEarnings");
    }

    private void TransformRawFinancialReports()
    {
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
                decimal? netIncome = CalcIncomeStatement_NetIncomeFromContinuingOperations(reportDate);
                if (netIncome is not null)
                    cashFlowReport.NetIncomeFromContinuingOperations = netIncome;
                else
                    LogMissingPropertyWarning(reportDate, "NetIncomeFromContinuingOperations");
            }

            if (rawCashFlowReport.HasValue("ChangesInCash"))
                cashFlowReport.GrossCashFlow = rawCashFlowReport["ChangesInCash"]!.Value;
            else if (rawCashFlowReport.HasValue("BeginningCashPosition") && rawCashFlowReport.HasValue("EndCashPosition"))
                cashFlowReport.GrossCashFlow = rawCashFlowReport["EndCashPosition"]!.Value - rawCashFlowReport["BeginningCashPosition"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "GrossCashFlow");

            if (rawCashFlowReport.HasValue("NetIssuancePaymentsOfDebt"))
                cashFlowReport.NetIssuanceOfDebt = rawCashFlowReport["NetIssuancePaymentsOfDebt"]!.Value;
            else if (rawCashFlowReport.HasValue("NetLongTermDebtIssuance"))
                cashFlowReport.NetIssuanceOfDebt = rawCashFlowReport["NetLongTermDebtIssuance"]!.Value;
            else
            {
                decimal? debtDiff = CalcConsecutiveBalanceSheetsChangeInDebt(reportDate);
                if (debtDiff is not null)
                    cashFlowReport.NetIssuanceOfDebt = debtDiff!.Value;
                else
                    LogMissingPropertyWarning(reportDate, "NetIssuanceOfDebt");
            }

            if (rawCashFlowReport.HasValue("NetCommonStockIssuance"))
                cashFlowReport.NetIssuanceOfStock = rawCashFlowReport["NetCommonStockIssuance"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfStock");

            if (rawCashFlowReport.HasValue("NetPreferredStockIssuance"))
                cashFlowReport.NetIssuanceOfPreferredStock = rawCashFlowReport["NetPreferredStockIssuance"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "NetIssuanceOfPreferredStock");

            if (rawCashFlowReport.HasValue("ChangeInWorkingCapital"))
                cashFlowReport.ChangeInWorkingCapital = rawCashFlowReport["ChangeInWorkingCapital"]!.Value;
            else
            {
                decimal? changeInWorkingCapital = CalcConsecutiveBalanceSheetsChangeInWorkingCapital(reportDate);
                if (changeInWorkingCapital is not null)
                    cashFlowReport.ChangeInWorkingCapital = changeInWorkingCapital.Value;
                else
                    LogMissingPropertyWarning(reportDate, "ChangeInWorkingCapital");
            }

            if (rawCashFlowReport.HasValue("Depreciation"))
                cashFlowReport.Depreciation = rawCashFlowReport["Depreciation"]!.Value;
            else if (rawCashFlowReport.HasValue("DepreciationAndAmortization"))
                cashFlowReport.Depreciation = rawCashFlowReport["DepreciationAndAmortization"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "depreciation");

            if (rawCashFlowReport.HasValue("Depletion"))
                cashFlowReport.Depletion = rawCashFlowReport["Depletion"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "depletion");

            if (rawCashFlowReport.HasValue("Amortization"))
                cashFlowReport.Amortization = rawCashFlowReport["Amortization"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "amortization");

            if (rawCashFlowReport.HasValue("DeferredTax"))
                cashFlowReport.DeferredTax = rawCashFlowReport["DeferredTax"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "defferedTax");

            if (rawCashFlowReport.HasValue("OtherNonCashItems"))
                cashFlowReport.OtherNonCashItems = rawCashFlowReport["OtherNonCashItems"]!.Value;
            else
                LogMissingPropertyWarning(reportDate, "otherNonCashItems");

            _annualProcessedCashFlowItems.Add(reportDate, cashFlowReport);
        }
    }

    /// <summary>
    /// Get the difference in debt between balance sheets for the given report date and the one balance sheet before it.
    /// </summary>
    private decimal? CalcConsecutiveBalanceSheetsChangeInDebt(DateOnly reportDate)
    {
        (RawReportDataMap? prevReport, RawReportDataMap? thisReport) = GetThisAndPrevBalanceSheets(reportDate);

        if (prevReport is null || thisReport is null)
        {
            return null;
        }

        decimal? prevDebt = CalcLongTermDebtFromRawBalanceSheet(reportDate, prevReport);
        decimal? curDebt = CalcLongTermDebtFromRawBalanceSheet(reportDate, thisReport);

        decimal? debtDiff = null;
        if (prevDebt is not null && curDebt is null)
            debtDiff = curDebt - prevDebt;

        return debtDiff;
    }

    /// <summary>
    /// Get the difference in working capital between balance sheets for the given report date and the one balance sheet before it.
    /// </summary>
    private decimal? CalcConsecutiveBalanceSheetsChangeInWorkingCapital(DateOnly reportDate)
    {
        (RawReportDataMap? prevReport, RawReportDataMap? thisReport) = GetThisAndPrevBalanceSheets(reportDate);

        if (prevReport is null || thisReport is null)
        {
            return null;
        }

        decimal? prevWorkingCapital = CalcWorkingCapitalFromRawBalanceSheet(reportDate, prevReport);
        decimal? curWorkingCapital = CalcWorkingCapitalFromRawBalanceSheet(reportDate, thisReport);

        decimal? changeInWorkingCapital = null;
        if (prevWorkingCapital is not null && curWorkingCapital is not null)
            changeInWorkingCapital = curWorkingCapital - prevWorkingCapital;

        return changeInWorkingCapital;
    }

    private decimal? CalcIncomeStatement_NetIncomeFromContinuingOperations(DateOnly reportDate)
    {
        var rpt = GetMatchingIncomeStatementByReportDate(reportDate);
        if (rpt is null)
            return null;

        return CalcNetIncomeFromContinuingOperationsFromRawIncomeStatement(reportDate, rpt);
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

    private decimal? CalcLongTermDebtFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet)
    {
        if (rawBalanceSheet.HasValue("LongTermDebtAndCapitalLeaseObligation"))
            return rawBalanceSheet["LongTermDebtAndCapitalLeaseObligation"]!.Value;
        else if (rawBalanceSheet.HasValue("LongTermDebt"))
            return rawBalanceSheet["LongTermDebt"]!.Value;
        else
        {
            //_logger.LogWarning("Balance sheet(report date: {ReportDate}) missing property 'LongTermDebtAndCapitalLeaseObligation' and 'LongTermDebt'",
            //    reportDate);
            return null;
        }
    }

    private decimal? CalcWorkingCapitalFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet)
    {
        decimal? currentAssets = CalcCurrentAssetsFromRawBalanceSheet(reportDate, rawBalanceSheet);
        decimal? currentLiabilities = CalcCurrentLiabilitiesFromRawBalanceSheet(reportDate, rawBalanceSheet);

        return currentAssets is not null && currentLiabilities is not null
            ? currentAssets - currentLiabilities
            : null;
    }

    private decimal? CalcCurrentAssetsFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet)
    {
        if (rawBalanceSheet.HasValue("CurrentAssets"))
        {
            return rawBalanceSheet["CurrentAssets"]!.Value;
        }
        else
        {
            //_logger.LogWarning("Balance sheet(report date: ${ReportDate}) missing property 'CurrentAssets'", reportDate);
            return null;
        }
    }

    private decimal? CalcCurrentLiabilitiesFromRawBalanceSheet(DateOnly reportDate, RawReportDataMap rawBalanceSheet)
    {
        if (rawBalanceSheet.HasValue("CurrentLiabilities"))
        {
            return rawBalanceSheet["CurrentLiabilities"]!.Value;
        }
        else
        {
            //_logger.LogWarning("Balance sheet(report date: ${ReportDate}) missing property 'CurrentLiabilities'", reportDate);
            return null;
        }
    }

    private decimal? CalcNetIncomeFromContinuingOperationsFromRawIncomeStatement(DateOnly reportDate, RawReportDataMap rawIncomeStatement)
    {
        if (rawIncomeStatement.HasValue("NetIncomeContinuousOperations"))
        {
            return rawIncomeStatement["NetIncomeContinuousOperations"]!.Value;
        }
        else
        {
            //_logger.LogWarning("Income statement(report date: ${ReportDate}) missing property 'NetIncomeContinuousOperations'", reportDate);
            return null;
        }
    }

    private void LogMissingPropertyWarning(DateOnly reportDate, string missingPropertyName)
    {
        // TODO
        //_logger.LogWarning("Missing property '${MissingPropertyName}'.Report date: ${ReportDate}",
        //    missingPropertyName, reportDate);
    }

    #endregion
}
