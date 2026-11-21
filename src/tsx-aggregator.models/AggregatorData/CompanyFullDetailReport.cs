using System;
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
        decimal curTotalShareholdersEquity,
        decimal curBookValue,
        long curNumShares,
        decimal averageNetCashFlow,
        decimal averageOwnerEarnings,
        decimal curDividendsPaid,
        decimal curAdjustedRetainedEarnings, // oldestRetainedEarnings + Dividends + Share buybacks
        decimal oldestRetainedEarnings,
        int numAnnualProcessedCashFlowReports,
        DateTimeOffset? mostRecentAnnualReportDate = null,
        DateTimeOffset? mostRecentQuarterlyReportDate = null,
        DateTimeOffset? lastUpdatedDate = null) {

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
        MostRecentAnnualReportDate = mostRecentAnnualReportDate;
        MostRecentQuarterlyReportDate = mostRecentQuarterlyReportDate;
        LastUpdatedDate = lastUpdatedDate;
    }

    public string Exchange { get; init; }
    public string CompanySymbol { get; init; }
    public string InstrumentSymbol { get; init; }
    public string CompanyName { get; init; }
    public string InstrumentName { get; init; }
    public decimal PricePerShare { get; init; }
    public decimal CurLongTermDebt { get; init; }
    public decimal CurTotalShareholdersEquity { get; init; }
    public decimal CurBookValue { get; init; }
    public long CurNumShares { get; init; }
    public decimal AverageNetCashFlow { get; init; }
    public decimal AverageOwnerEarnings { get; init; }
    public decimal CurDividendsPaid { get; init; }
    public decimal CurAdjustedRetainedEarnings { get; init; }
    public decimal OldestRetainedEarnings { get; init; }
    public int NumAnnualProcessedCashFlowReports { get; init; }
    public DateTimeOffset? MostRecentAnnualReportDate { get; init; }
    public DateTimeOffset? MostRecentQuarterlyReportDate { get; init; }
    public DateTimeOffset? LastUpdatedDate { get; init; }

    // Calculated Properties
    public decimal CurMarketCap => CurNumShares * PricePerShare;
    public decimal DebtToEquityRatio => Utilities.DivSafe(CurLongTermDebt, CurTotalShareholdersEquity);
    public decimal CurPriceToBookRatio => Utilities.DivSafe(CurMarketCap, CurBookValue);
    public decimal LongTermDebtToBookRatio => Utilities.DivSafe(CurLongTermDebt, CurBookValue);
    public bool DidAdjustedRetainedEarningsIncrease => CurAdjustedRetainedEarnings > OldestRetainedEarnings;
    public decimal ReturnOnEquity_FromCashFlow => Utilities.DivSafe(AverageNetCashFlow, CurTotalShareholdersEquity);
    public decimal ReturnOnEquity_FromOwnerEarnings => Utilities.DivSafe(AverageOwnerEarnings, CurTotalShareholdersEquity);
    public decimal EstimatedNextYearBookValue_FromCashFlow {
        get {
            return CurBookValue == decimal.MinValue || AverageNetCashFlow == decimal.MinValue
                ? decimal.MinValue
                : CurBookValue + AverageNetCashFlow;
        }
    }
    public decimal EstimatedNextYearTotalReturnPercentage_FromCashFlow {
        get {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || CurBookValue == decimal.MinValue) {
                return decimal.MinValue;
            }

            // Net cash flow is after dividends (a financing outflow) but before buybacks, so dividends are added
            // back: cash generated for shareholders, however it is returned.
            return 100M * (AverageNetCashFlow + CurDividendsPaid) / CurMarketCap;
        }
    }
    public decimal EstimatedNextYearBookValue_FromOwnerEarnings {
        get {
            return CurBookValue == decimal.MinValue || AverageOwnerEarnings == decimal.MinValue
                ? decimal.MinValue
                : CurBookValue + AverageOwnerEarnings;
        }
    }
    public decimal EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings {
        get {
            if (CurMarketCap == 0
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue) {
                return decimal.MinValue;
            }

            // Owner earnings are pre-dividend, so all of it is the shareholders' return.
            return 100M * AverageOwnerEarnings / CurMarketCap;
        }
    }

    public decimal MaxPrice {
        get {
            if (CurNumShares <= 0
                || EstimatedNextYearBookValue_FromCashFlow == decimal.MinValue
                || EstimatedNextYearBookValue_FromOwnerEarnings == decimal.MinValue
                || CurBookValue == decimal.MinValue) {
                return -1;
            }

            decimal maxPriceSoFar = 3M * CurBookValue;
            // The price at which each est. return is exactly 5%: same numerators as the returns.
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * (AverageNetCashFlow + CurDividendsPaid));
            maxPriceSoFar = Math.Min(maxPriceSoFar, 20M * AverageOwnerEarnings);

            return maxPriceSoFar / CurNumShares;
        }
    }

    public decimal PercentageUpside {
        get {
            if (PricePerShare <= 0 || MaxPrice == -1)
                return decimal.MinValue;
            return (MaxPrice - PricePerShare) / PricePerShare * 100M;
        }
    }

    // Scores
    // Sign rule (decision 4): a check whose denominator is equity or book value fails when that denominator is
    // 0 or less. A negative denominator flips the ratio's meaning; the ratios themselves are left as they are.
    private bool HasPositiveEquity => CurTotalShareholdersEquity > 0;
    private bool HasPositiveBookValue => CurBookValue > 0;

    public bool DoesPassCheck_DebtToEquitySmallEnough => HasPositiveEquity && DebtToEquityRatio < 0.5M;
    public bool DoesPassCheck_BookValueBigEnough => CurBookValue > 150_000_000M;
    public bool DoesPassCheck_PriceToBookSmallEnough => HasPositiveBookValue && CurPriceToBookRatio <= 3M;
    public bool DoesPassCheck_AvgCashFlow_Positive => AverageNetCashFlow > 0;
    public bool DoesPassCheck_AvgOwnerEarningsPositive => AverageOwnerEarnings > 0;
    public bool DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough => EstimatedNextYearTotalReturnPercentage_FromCashFlow > 5M;
    public bool DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough => EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings > 5M;
    public bool DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig => EstimatedNextYearTotalReturnPercentage_FromCashFlow < 40M;
    public bool DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig => EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings < 40M;
    public bool DoesPassCheck_DebtToBookRatioSmallEnough => HasPositiveBookValue && LongTermDebtToBookRatio < 1M;
    public bool DoesPassCheck_RetainedEarningsPositive => CurAdjustedRetainedEarnings > 0;
    public bool DoesPassCheck_IsHistoryLongEnough => NumAnnualProcessedCashFlowReports >= 4;
    public bool DoesPassCheck_ROE_FromCashFlow_BigEnough => HasPositiveEquity && ReturnOnEquity_FromCashFlow > 0.10M;
    public bool DoesPassCheck_ROE_FromOwnerEarnings_BigEnough => HasPositiveEquity && ReturnOnEquity_FromOwnerEarnings > 0.10M;
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
        && DidAdjustedRetainedEarningsIncrease
        && DoesPassCheck_ROE_FromCashFlow_BigEnough
        && DoesPassCheck_ROE_FromOwnerEarnings_BigEnough;
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
        + (DidAdjustedRetainedEarningsIncrease ? 1 : 0)
        + (DoesPassCheck_ROE_FromCashFlow_BigEnough ? 1 : 0)
        + (DoesPassCheck_ROE_FromOwnerEarnings_BigEnough ? 1 : 0);
}
