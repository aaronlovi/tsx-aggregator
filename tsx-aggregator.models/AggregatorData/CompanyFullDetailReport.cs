﻿using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public class CompanyFullDetailReport {
    public CompanyFullDetailReport(long id, decimal pricePerShare, CompanyReport companyReport) {
        Id = id;
        PricePerShare = pricePerShare;
        CompanyReport = companyReport;
    }

    public long Id { get; init; }
    public decimal PricePerShare { get; init; }
    public CompanyReport CompanyReport { get; init; }

    // From CompanyReport
    public decimal CurLongTermDebt => CompanyReport.CurLongTermDebt;
    public decimal CurTotalShareholdersEquity => CompanyReport.CurTotalShareholdersEquity;
    public decimal CurBookValue => CompanyReport.CurBookValue;
    public long CurNumShares => CompanyReport.CurNumShares;
    public decimal AverageNetCashFlow => CompanyReport.AverageNetCashFlow;
    public decimal AverageOwnerEarnings => CompanyReport.AverageOwnerEarnings;
    public decimal CurDividendsPaid => CompanyReport.CurDividendsPaid;
    public decimal LongTermDebtToBookRatio => CompanyReport.LongTermDebtToBookRatio;
    public decimal CurRetainedEarnings => CompanyReport.CurRetainedEarnings;
    public decimal OldestRetainedEarnings => CompanyReport.OldestRetainedEarnings;
    public int NumAnnualProcessedCashFlowReports => CompanyReport.NumAnnualProcessedCashFlowReports;
    public decimal CurMarketCap => CurNumShares * PricePerShare;

    // Calculated Properties
    public decimal DebtToEquityRatio => Utilities.DivSafe(CurLongTermDebt, CurTotalShareholdersEquity);
    public decimal CurPriceToBookRatio => Utilities.DivSafe(CurMarketCap, CurBookValue);
    public bool DidRetainedEarningsIncrease => CurRetainedEarnings > OldestRetainedEarnings;
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

            return 100M * (EstimatedNextYearBookValue_FromCashFlow - CurDividendsPaid - CurBookValue) / CurMarketCap;
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

            return 100M * (EstimatedNextYearBookValue_FromOwnerEarnings - CurDividendsPaid - CurBookValue) / CurMarketCap;
        }
    }

    // Scores
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
    public bool DoesPassCheck_IsHistoryLongEnough => NumAnnualProcessedCashFlowReports >= 4;
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
}
