export class InstrumentRawReportData {
    instrumentReportId: number;
    reportCreatedDate: Date;
    isCurrent: boolean;
    checkManually: boolean;
    ignoreReport: boolean;
    reportJson: string;

    constructor(
        instrumentReportId: number,
        reportCreatedDate: Date,
        isCurrent: boolean,
        checkManually: boolean,
        ignoreReport: boolean,
        reportJson: string) {
        this.instrumentReportId = instrumentReportId;
        this.reportCreatedDate = reportCreatedDate;
        this.isCurrent = isCurrent;
        this.checkManually = checkManually;
        this.ignoreReport = ignoreReport;
        this.reportJson = reportJson;
    }

    parseReportJson(): { [key: string]: number } {
        try {
            const parsedJson = JSON.parse(this.reportJson);
            const result: { [key: string]: number } = {};

            for (const key in parsedJson) {
                if (parsedJson.hasOwnProperty(key) && typeof parsedJson[key] === 'number') {
                    result[key] = parsedJson[key];
                }
            }

            return result;
        } catch (error) {
            console.error('Failed to parse reportJson:', error);
            return {};
        }
    }
}
