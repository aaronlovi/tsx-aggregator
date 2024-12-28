import { InstrumentRawReportData } from "./instrument_raw_report_data";

export class InstrumentWithConflictingRawData {
    instrumentId: number;
    exchange: string;
    companySymbol: string;
    instrumentSymbol: string;
    companyName: string;
    instrumentName: string;
    reportType: number;
    reportPeriodType: number;
    reportDate: Date;
    conflictingRawReports: InstrumentRawReportData[];

    constructor(
        instrumentId: number,
        exchange: string,
        companySymbol: string,
        instrumentSymbol: string,
        companyName: string,
        instrumentName: string,
        reportType: number,
        reportPeriodType: number,
        reportDate: Date,
        conflictingRawReports: InstrumentRawReportData[]) {
        this.instrumentId = instrumentId;
        this.exchange = exchange;
        this.companySymbol = companySymbol;
        this.instrumentSymbol = instrumentSymbol;
        this.companyName = companyName;
        this.instrumentName = instrumentName;
        this.reportType = reportType;
        this.reportPeriodType = reportPeriodType;
        this.reportDate = reportDate;
        this.conflictingRawReports = conflictingRawReports;
    }
}