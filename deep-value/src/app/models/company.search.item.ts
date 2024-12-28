export class CompanySearchItem {
    exchange: string;
    instrumentSymbol: string;
    companyName: string;

    constructor(
        exchange: string,
        instrumentSymbol: string,
        companyName: string) {
        this.exchange = exchange;
        this.instrumentSymbol = instrumentSymbol;
        this.companyName = companyName;
    }
}
