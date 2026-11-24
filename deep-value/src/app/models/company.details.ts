/**
 * A company's full details as returned by GET companies/{exchange}/{instrumentSymbol}.
 *
 * Every ratio, check result, the score and the max price are computed by the backend
 * (CompanyFullDetailReport.cs); this model only carries them for display. Property names match the
 * API's JSON exactly. The contract is pinned by src/testing/company-full-detail-report.json, which
 * both the backend (CompanyDetailsApiContractTests) and company.service.spec.ts test against (plus
 * company-full-detail-report-missing-data.json for the n/a path).
 *
 * A null number means the source data didn't have the figure; a check is 'NotAvailable' when one of its inputs is
 * null. The score counts 'Pass' only.
 */
export type CheckResult = 'Pass' | 'Fail' | 'NotAvailable';

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
    curShortTermDebt: number;
    curTotalDebt: number;
    curTotalShareholdersEquity: number | null;
    curBookValue: number | null;
    curNumShares: number;
    averageNetCashFlow: number | null;
    averageOwnerEarnings: number | null;
    latestOwnerEarnings: number | null;
    curDividendsPaid: number | null;
    averageDividendsPaid: number | null;
    curAdjustedRetainedEarnings: number | null;
    oldestRetainedEarnings: number | null;
    numAnnualProcessedCashFlowReports: number;
    mostRecentAnnualReportDate: string | null;
    mostRecentQuarterlyReportDate: string | null;
    lastUpdatedDate: string | null;
    /** Retained earnings missing from the provider's balance sheet, estimated with net income - dividends. */
    curRetainedEarningsEstimated: boolean;
    oldestRetainedEarningsEstimated: boolean;

    // Computed by the backend
    curMarketCap: number | null;
    debtToEquityRatio: number | null;
    curPriceToBookRatio: number | null;
    longTermDebtToBookRatio: number | null;
    returnOnEquity_FromCashFlow: number | null;
    returnOnEquity_FromOwnerEarnings: number | null;
    estimatedNextYearBookValue_FromCashFlow: number | null;
    estimatedNextYearTotalReturnPercentage_FromCashFlow: number | null;
    estimatedNextYearBookValue_FromOwnerEarnings: number | null;
    estimatedNextYearTotalReturnPercentage_FromOwnerEarnings: number | null;
    maxPrice: number | null;
    percentageUpside: number | null;

    // Checks, in the order the backend's OverallScore counts them
    doesPassCheck_DebtToEquitySmallEnough: CheckResult;
    doesPassCheck_BookValueBigEnough: CheckResult;
    doesPassCheck_PriceToBookSmallEnough: CheckResult;
    doesPassCheck_AvgCashFlow_Positive: CheckResult;
    doesPassCheck_AvgOwnerEarningsPositive: CheckResult;
    doesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough: CheckResult;
    doesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough: CheckResult;
    doesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig: CheckResult;
    doesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig: CheckResult;
    doesPassCheck_DebtToBookRatioSmallEnough: CheckResult;
    doesPassCheck_RetainedEarningsPositive: CheckResult;
    doesPassCheck_IsHistoryLongEnough: CheckResult;
    didAdjustedRetainedEarningsIncrease: CheckResult; // the 13th check
    doesPassCheck_ROE_FromCashFlow_BigEnough: CheckResult;
    doesPassCheck_ROE_FromOwnerEarnings_BigEnough: CheckResult;
    /**
     * Check 16 (C16): ROE (OE) over 10% in at least roeConsistencyMinYears of the last roeConsistencyWindowYears;
     * with fewer usable years (but at least roeConsistencyMinUsableYears), the same number of misses is allowed.
     */
    doesPassCheck_ROE_FromOwnerEarnings_Consistent: CheckResult;
    /** Usable years check 16 looked at; null when there were too few (the check is then n/a). */
    roeOwnerEarningsYearsConsidered: number | null;
    /** How many of those years had ROE (OE) over 10%. */
    roeOwnerEarningsYearsAbove10: number | null;
    /** How many of them had to be over 10%. */
    roeOwnerEarningsYearsRequired: number | null;
    roeConsistencyMinYears: number;
    roeConsistencyWindowYears: number;
    roeConsistencyMinUsableYears: number;
    doesPassCheck_Overall: boolean;
    overallScore: number;
    notAvailableCount: number;
}
