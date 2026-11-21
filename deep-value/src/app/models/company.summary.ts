/**
 * A row of the company lists (GET companies, companies/bottom, companies/all). Everything is computed by the
 * backend (CompanySummaryReport.cs). A null number means the source data didn't have the figure.
 */
export class CompanySummary {
    exchange: string;
    instrumentSymbol: string;
    companyName: string;
    pricePerShare: number;
    curMarketCap: number | null;
    estimatedNextYearTotalReturnPercentageFromCashFlow: number | null;
    estimatedNextYearTotalReturnPercentageOwnerEarnings: number | null;
    overallScore: number;
    /** How many of the 15 checks had missing data (they earn no point). */
    notAvailableCount: number;
    maxPrice: number | null;
    percentageUpside: number | null;
    returnOnEquity_FromCashFlow: number | null;
    returnOnEquity_FromOwnerEarnings: number | null;
    mostRecentAnnualReportDate: string | null;
    mostRecentQuarterlyReportDate: string | null;
    lastUpdatedDate: string | null;

    constructor(item: any) {
        this.exchange = item.exchange;
        this.instrumentSymbol = item.instrumentSymbol;
        this.companyName = item.companyName;
        this.pricePerShare = item.pricePerShare;
        this.curMarketCap = item.curMarketCap ?? null;
        this.estimatedNextYearTotalReturnPercentageFromCashFlow = item.estimatedNextYearTotalReturnPercentage_FromCashFlow ?? null;
        this.estimatedNextYearTotalReturnPercentageOwnerEarnings = item.estimatedNextYearTotalReturnPercentage_FromOwnerEarnings ?? null;
        this.overallScore = item.overallScore;
        this.notAvailableCount = item.notAvailableCount ?? 0;
        this.maxPrice = item.maxPrice ?? null;
        this.percentageUpside = item.percentageUpside ?? null;
        this.returnOnEquity_FromCashFlow = item.returnOnEquity_FromCashFlow ?? null;
        this.returnOnEquity_FromOwnerEarnings = item.returnOnEquity_FromOwnerEarnings ?? null;
        this.mostRecentAnnualReportDate = item.mostRecentAnnualReportDate ?? null;
        this.mostRecentQuarterlyReportDate = item.mostRecentQuarterlyReportDate ?? null;
        this.lastUpdatedDate = item.lastUpdatedDate ?? null;
    }
}
