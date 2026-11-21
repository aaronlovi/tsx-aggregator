/**
 * A company's full details as returned by GET companies/{exchange}/{instrumentSymbol}.
 *
 * Every ratio, check result, the score and the max price are computed by the backend
 * (CompanyFullDetailReport.cs); this model only carries them for display. Property names match the
 * API's JSON exactly. The contract is pinned by src/testing/company-full-detail-report.json, which
 * both the backend (CompanyDetailsApiContractTests) and company.service.spec.ts test against.
 */
export interface CompanyDetails {
    // Identity
    exchange: string;
    companySymbol: string;
    instrumentSymbol: string;
    companyName: string;
    instrumentName: string;

    // Inputs
    pricePerShare: number;
    curLongTermDebt: number;
    curTotalShareholdersEquity: number;
    curBookValue: number;
    curNumShares: number;
    averageNetCashFlow: number;
    averageOwnerEarnings: number;
    curDividendsPaid: number;
    curAdjustedRetainedEarnings: number;
    oldestRetainedEarnings: number;
    numAnnualProcessedCashFlowReports: number;
    mostRecentAnnualReportDate: string | null;
    mostRecentQuarterlyReportDate: string | null;
    lastUpdatedDate: string | null;

    // Computed by the backend
    curMarketCap: number;
    debtToEquityRatio: number;
    curPriceToBookRatio: number;
    longTermDebtToBookRatio: number;
    didAdjustedRetainedEarningsIncrease: boolean;
    returnOnEquity_FromCashFlow: number;
    returnOnEquity_FromOwnerEarnings: number;
    estimatedNextYearBookValue_FromCashFlow: number;
    estimatedNextYearTotalReturnPercentage_FromCashFlow: number;
    estimatedNextYearBookValue_FromOwnerEarnings: number;
    estimatedNextYearTotalReturnPercentage_FromOwnerEarnings: number;
    maxPrice: number;
    percentageUpside: number;

    // Checks, in the order the backend's OverallScore counts them
    doesPassCheck_DebtToEquitySmallEnough: boolean;
    doesPassCheck_BookValueBigEnough: boolean;
    doesPassCheck_PriceToBookSmallEnough: boolean;
    doesPassCheck_AvgCashFlow_Positive: boolean;
    doesPassCheck_AvgOwnerEarningsPositive: boolean;
    doesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough: boolean;
    doesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough: boolean;
    doesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig: boolean;
    doesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig: boolean;
    doesPassCheck_DebtToBookRatioSmallEnough: boolean;
    doesPassCheck_RetainedEarningsPositive: boolean;
    doesPassCheck_IsHistoryLongEnough: boolean;
    // didAdjustedRetainedEarningsIncrease (above) is the 13th check
    doesPassCheck_ROE_FromCashFlow_BigEnough: boolean;
    doesPassCheck_ROE_FromOwnerEarnings_BigEnough: boolean;
    doesPassCheck_Overall: boolean;
    overallScore: number;
}
