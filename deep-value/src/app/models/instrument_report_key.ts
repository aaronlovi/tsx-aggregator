export class InstrumentReportKey {
    constructor(
        public instrumentId: number,
        public reportType: number,
        public reportPeriodType: number,
        public reportDate: Date
    ) {}

    toString() : string {
        return `${this.instrumentId}-${this.reportType}-${this.reportPeriodType}-${this.reportDate.toISOString()}`;
    }
}
