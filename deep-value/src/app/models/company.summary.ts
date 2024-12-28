export class CompanySummary {
    exchange: string;
    instrumentSymbol: string;
    companyName: string;
    pricePerShare: number;
    curMarketCap: number;
    estimatedNextYearTotalReturnPercentageFromCashFlow: number;
    estimatedNextYearTotalReturnPercentageOwnerEarnings: number;
    overallScore: number;

    constructor(
        exchange: string,
        instrumentSymbol: string,
        companyName: string,
        pricePerShare: number,
        curMarketCap: number,
        estimatedNextYearTotalReturnPercentageFromCashFlow: number,
        estimatedNextYearTotalReturnPercentageOwnerEarnings: number,
        overallScore: number) {
            this.exchange = exchange;
            this.instrumentSymbol = instrumentSymbol;
            this.companyName = companyName;
            this.pricePerShare = pricePerShare;
            this.curMarketCap = curMarketCap;
            this.estimatedNextYearTotalReturnPercentageFromCashFlow = estimatedNextYearTotalReturnPercentageFromCashFlow;
            this.estimatedNextYearTotalReturnPercentageOwnerEarnings = estimatedNextYearTotalReturnPercentageOwnerEarnings;
            this.overallScore = overallScore;
    }
}
