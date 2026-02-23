export class CompanySummary {
    exchange: string;
    instrumentSymbol: string;
    companyName: string;
    pricePerShare: number;
    curMarketCap: number;
    estimatedNextYearTotalReturnPercentageFromCashFlow: number;
    estimatedNextYearTotalReturnPercentageOwnerEarnings: number;
    overallScore: number;
    maxPrice: number;
    returnOnEquity_FromCashFlow: number;
    returnOnEquity_FromOwnerEarnings: number;

    constructor(
        exchange: string,
        instrumentSymbol: string,
        companyName: string,
        pricePerShare: number,
        curMarketCap: number,
        estimatedNextYearTotalReturnPercentageFromCashFlow: number,
        estimatedNextYearTotalReturnPercentageOwnerEarnings: number,
        overallScore: number,
        maxPrice: number,
        returnOnEquity_FromCashFlow: number,
        returnOnEquity_FromOwnerEarnings: number) {
            this.exchange = exchange;
            this.instrumentSymbol = instrumentSymbol;
            this.companyName = companyName;
            this.pricePerShare = pricePerShare;
            this.curMarketCap = curMarketCap;
            this.estimatedNextYearTotalReturnPercentageFromCashFlow = estimatedNextYearTotalReturnPercentageFromCashFlow;
            this.estimatedNextYearTotalReturnPercentageOwnerEarnings = estimatedNextYearTotalReturnPercentageOwnerEarnings;
            this.overallScore = overallScore;
            this.maxPrice = maxPrice;
            this.returnOnEquity_FromCashFlow = returnOnEquity_FromCashFlow;
            this.returnOnEquity_FromOwnerEarnings = returnOnEquity_FromOwnerEarnings;
    }

    public get percentageUpside() {
        if (this.pricePerShare <= 0 || this.maxPrice === -1)
            return Number.MIN_VALUE;
        return (this.maxPrice - this.pricePerShare) / this.pricePerShare * 100.0;
    }
}
