import { Utilities } from "../shared/utilities";

export class CompanyDetails {
    exchange: string;
    companySymbol: string;
    instrumentSymbol: string;
    companyName: string;
    instrumentName: string;
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

    constructor(
        exchange: string,
        companySymbol: string,
        instrumentSymbol: string,
        companyName: string,
        instrumentName: string,
        pricePerShare: number,
        curLongTermDebt: number,
        curTotalShareholdersEquity: number,
        curBookValue: number,
        curNumShares: number,
        averageNetCashFlow: number,
        averageOwnerEarnings: number,
        curDividendsPaid: number,
        curAdjustedRetainedEarnings: number,
        oldestRetainedEarnings: number,
        numAnnualProcessedCashFlowReports: number) {
        this.exchange = exchange;
        this.companySymbol = companySymbol;
        this.instrumentSymbol = instrumentSymbol;
        this.companyName = companyName;
        this.instrumentName = instrumentName;
        this.pricePerShare = pricePerShare;
        this.curLongTermDebt = curLongTermDebt;
        this.curTotalShareholdersEquity = curTotalShareholdersEquity;
        this.curBookValue = curBookValue;
        this.curNumShares = curNumShares;
        this.averageNetCashFlow = averageNetCashFlow;
        this.averageOwnerEarnings = averageOwnerEarnings;
        this.curDividendsPaid = curDividendsPaid;
        this.curAdjustedRetainedEarnings = curAdjustedRetainedEarnings;
        this.oldestRetainedEarnings = oldestRetainedEarnings;
        this.numAnnualProcessedCashFlowReports = numAnnualProcessedCashFlowReports;
    }

    // Calculated Properties
    public get curMarketCap() { return this.curNumShares * this.pricePerShare; }
    public get debtToEquityRatio() { return Utilities.DivSafe(this.curLongTermDebt, this.curTotalShareholdersEquity); }
    public get curPriceToBookRatio() { return Utilities.DivSafe(this.curMarketCap, this.curBookValue); }
    public get longTermDebtToBookRatio() { return Utilities.DivSafe(this.curLongTermDebt, this.curBookValue); }
    public get didAdjustedRetainedEarningsIncrease() { return this.curAdjustedRetainedEarnings > this.oldestRetainedEarnings; }
    public get estimatedNextYearBookValue_FromCashFlow() {
        return this.curBookValue === Number.MIN_VALUE || this.averageNetCashFlow === Number.MIN_VALUE
            ? Number.MIN_VALUE
            : this.curBookValue + this.averageNetCashFlow;
    }
    public get estimatedNextYearTotalReturnPercentage_FromCashFlow() {
        if (this.curMarketCap === 0
            || this.estimatedNextYearBookValue_FromCashFlow === Number.MIN_VALUE
            || this.curBookValue === Number.MIN_VALUE) {
            return Number.MIN_VALUE;
        }

        return 100.0 * (this.estimatedNextYearBookValue_FromCashFlow - this.curDividendsPaid - this.curBookValue) / this.curMarketCap;
    }
    public get estimatedNextYearBookValueFromOwnerEarnings() {
        return this.curBookValue === Number.MIN_VALUE || this.averageOwnerEarnings === Number.MIN_VALUE
            ? Number.MIN_VALUE
            : this.curBookValue + this.averageOwnerEarnings;
    }
    public get estimatedNextYearTotalReturnPercentageFromOwnerEarnings() {
        if (this.curMarketCap === 0
            || this.estimatedNextYearBookValueFromOwnerEarnings === Number.MIN_VALUE
            || this.curBookValue === Number.MIN_VALUE) {
            return Number.MIN_VALUE;
        }

        return 100.0 * (this.estimatedNextYearBookValueFromOwnerEarnings - this.curDividendsPaid - this.curBookValue) / this.curMarketCap;
    }

    // Scores
    public get doesPassCheckDebtToEquitySmallEnough() { return this.debtToEquityRatio < 0.5; }
    public get doesPassCheckBookValueBigEnough() { return this.curBookValue > 150_000_000; }
    public get doesPassCheckPriceToBookSmallEnough() { return this.curPriceToBookRatio <= 3; }
    public get doesPassCheckAvgCashFlow_Positive() { return this.averageNetCashFlow > 0; }
    public get doesPassCheckAvgOwnerEarningsPositive() { return this.averageOwnerEarnings > 0; }
    public get doesPassCheckEstNextYearTotalReturn_CashFlow_BigEnough() { return this.estimatedNextYearTotalReturnPercentage_FromCashFlow > 5; }
    public get doesPassCheckEstNextYeartotalReturn_OwnerEarnings_BigEnough() { return this.estimatedNextYearTotalReturnPercentageFromOwnerEarnings > 5; }
    public get doesPassCheckEstNextYearTotalReturn_CashFlow_NotTooBig() { return this.estimatedNextYearTotalReturnPercentage_FromCashFlow < 40; }
    public get doesPassCheckEstNextYeartotalReturn_OwnerEarnings_NotTooBig() { return this.estimatedNextYearTotalReturnPercentageFromOwnerEarnings < 40; }
    public get doesPassCheckDebtToBookRatioSmallEnough() { return this.longTermDebtToBookRatio < 1; }
    public get doesPassCheckAdjustedRetainedEarningsPositive() { return this.curAdjustedRetainedEarnings > 0; }
    public get doesPassCheckIsHistoryLongEnough() { return this.numAnnualProcessedCashFlowReports >= 4; }
    public get doesPassCheckOverall() {
        return this.doesPassCheckDebtToEquitySmallEnough
            && this.doesPassCheckBookValueBigEnough
            && this.doesPassCheckPriceToBookSmallEnough
            && this.doesPassCheckAvgCashFlow_Positive
            && this.doesPassCheckAvgOwnerEarningsPositive
            && this.doesPassCheckEstNextYearTotalReturn_CashFlow_BigEnough
            && this.doesPassCheckEstNextYeartotalReturn_OwnerEarnings_BigEnough
            && this.doesPassCheckEstNextYearTotalReturn_CashFlow_NotTooBig
            && this.doesPassCheckEstNextYeartotalReturn_OwnerEarnings_NotTooBig
            && this.doesPassCheckDebtToBookRatioSmallEnough
            && this.doesPassCheckAdjustedRetainedEarningsPositive
            && this.doesPassCheckIsHistoryLongEnough
            && this.didAdjustedRetainedEarningsIncrease;
    }
    public get overallScore() {
        return (this.doesPassCheckDebtToEquitySmallEnough ? 1 : 0)
            + (this.doesPassCheckBookValueBigEnough ? 1 : 0)
            + (this.doesPassCheckPriceToBookSmallEnough ? 1 : 0)
            + (this.doesPassCheckAvgCashFlow_Positive ? 1 : 0)
            + (this.doesPassCheckAvgOwnerEarningsPositive ? 1 : 0)
            + (this.doesPassCheckEstNextYearTotalReturn_CashFlow_BigEnough ? 1 : 0)
            + (this.doesPassCheckEstNextYeartotalReturn_OwnerEarnings_BigEnough ? 1 : 0)
            + (this.doesPassCheckEstNextYearTotalReturn_CashFlow_NotTooBig ? 1 : 0)
            + (this.doesPassCheckEstNextYeartotalReturn_OwnerEarnings_NotTooBig ? 1 : 0)
            + (this.doesPassCheckDebtToBookRatioSmallEnough ? 1 : 0)
            + (this.doesPassCheckAdjustedRetainedEarningsPositive ? 1 : 0)
            + (this.doesPassCheckIsHistoryLongEnough ? 1 : 0)
            + (this.didAdjustedRetainedEarningsIncrease ? 1 : 0);
    }
}
