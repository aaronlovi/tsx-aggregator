import { Component, OnInit } from "@angular/core";
import { InstrumentRawReportData } from "../models/instrument_raw_report_data";
import { InstrumentsWithConflictingRawData } from "../models/instruments_with_conflicting_raw_data";
import { CompanyService } from "../services/company.service";
import { getReportTypeString, getReportPeriodTypeString } from '../models/constants';
import { InstrumentReportKey } from "../models/instrument_report_key";
import { InstrumentWithConflictingRawData } from "../models/instrument_with_conflicting_raw_data";
import { catchError, concatMap, from, Observable, of, Subscription, tap } from "rxjs";

@Component({
    selector: 'app-updated-raw-data-reports',
    templateUrl: './updated-raw-data-reports.html',
    styleUrls: ['./updated-raw-data-reports.scss']
})
export class UpdatedRawDataReportsComponent implements OnInit {
    data: InstrumentsWithConflictingRawData | null = null;
    error: string | null = null;
    validationErrors: { [key: string]: string | null } = {};
    successMessages: { [key: string]: string | null } = {};
    isValueDifferentCache: { [key: string]: { [key: string]: boolean } } = {};
    expandedInstruments: Set<string> = new Set<string>();
    currentPage: number = 1;
    pageSize: number = 10;
    pageSizes: number[] = [5, 10, 20, 50, 100];
    reportActions: { [key: string]: { [reportId: number]: string } } = {};
    isIgnoringReports: boolean = false;
    completedCount: number = 0;
    errorCount: number = 0;
    private subscriptions: Subscription = new Subscription();
    
    // Imported functions
    getReportTypeString = getReportTypeString;
    getReportPeriodTypeString = getReportPeriodTypeString;

    constructor(private companyService: CompanyService) { }

    ngOnInit(): void {
        this.fetchData('TSX', 1, 10);
    }

    ngOnDestroy(): void {
        this.subscriptions.unsubscribe();
    }

    fetchData(exchange: string, pageNumber: number, pageSize: number): void {
        this.expandedInstruments.clear();
        this.isValueDifferentCache = {};
        this.error = '';
        this.validationErrors = {};
        this.successMessages = {};
        this.data = null;
        this.reportActions = {};
        this.isIgnoringReports = false;
        this.completedCount = 0;
        this.errorCount = 0;
        this.subscriptions.unsubscribe();
        this.companyService.getUpdatedRawDataReports(exchange, pageNumber, pageSize).subscribe({
            next: (data) => {
                this.data = data;
                this.initializeReportActions();
                console.log(data);
            },
            error: (err) => this.error = err.message
        });
    }

    initializeReportActions(): void {
        if (!this.data) return;

        this.data.instrumentWithConflictingRawData.forEach(instrument => {
            const key = this.getInstrumentKey(instrument);
            const keyStr = key.toString();
            if (!this.reportActions[keyStr]) {
                this.reportActions[keyStr] = {};
            }
            instrument.conflictingRawReports.forEach((report, index) => {
                this.reportActions[keyStr][report.instrumentReportId] = index === instrument.conflictingRawReports.length - 1 ? 'keep' : 'ignore';
            });
        });
    }

    getKeys(report: InstrumentRawReportData): string[] {
        return Object.keys(report.parseReportJson());
    }

    getAllKeys(reports: InstrumentRawReportData[]): string[] {
        const allKeys = new Set<string>();
        reports.forEach(report => {
            const keys = Object.keys(report.parseReportJson());
            keys.forEach(key => allKeys.add(key));
        });
        return Array.from(allKeys);
    }

    isValueDifferent(key: string, reports: InstrumentRawReportData[], instrument: InstrumentWithConflictingRawData): boolean {
        const compositeKey = this.getInstrumentKey(instrument).toString();
        if (!this.isValueDifferentCache[compositeKey]) {
            this.isValueDifferentCache[compositeKey] = {};
        }

        if (this.isValueDifferentCache[compositeKey][key] === undefined) {
            const values = reports.map(report => report.parseReportJson()[key]);
            this.isValueDifferentCache[compositeKey][key] = new Set(values).size > 1;
        }

        return this.isValueDifferentCache[compositeKey][key];
    }

    toggleCollapse(instrument: InstrumentWithConflictingRawData): void {
        const key = this.getInstrumentKey(instrument);
        const keyStr = key.toString();
        if (this.expandedInstruments.has(keyStr)) {
            this.expandedInstruments.delete(keyStr);
        } else {
            this.expandedInstruments.add(keyStr);
        }
    }

    isCollapsed(instrument: InstrumentWithConflictingRawData): boolean {
        const key = this.getInstrumentKey(instrument);
        return !this.expandedInstruments.has(key.toString());
    }

    getInstrumentKey(instrument: InstrumentWithConflictingRawData): InstrumentReportKey {
        return new InstrumentReportKey(instrument.instrumentId, instrument.reportType, instrument.reportPeriodType, instrument.reportDate);
    }

    expandAll(): void {
        if (!this.data) return;

        this.data.instrumentWithConflictingRawData.forEach(i => {
            const key = this.getInstrumentKey(i);
            this.expandedInstruments.add(key.toString());
        })
    }

    collapseAll(): void {
        this.expandedInstruments.clear();
    }

    goToFirstPage(): void {
        this.currentPage = 1;
        this.fetchData('TSX', this.currentPage, this.pageSize);
    }

    goToPreviousPage(): void {
        if (this.currentPage <= 1) return;

        this.currentPage--;
        this.fetchData('TSX', this.currentPage, this.pageSize);
    }

    goToNextPage(): void {
        if (!this.data || this.data.instrumentWithConflictingRawData.length < this.pageSize) return;

        this.currentPage++;
        this.fetchData('TSX', this.currentPage, this.pageSize);
    }

    goToLastPage(): void {
        if (!this.data) return;

        this.currentPage = Math.ceil(this.data.pagingData.numPages);
        this.fetchData('TSX', this.currentPage, this.pageSize);
    }

    onPageSizeChange(event: Event): void {
        this.currentPage = 1; // Reset to first page
        this.fetchData('TSX', this.currentPage, this.pageSize);
    }

    updateReportAction(instrument: any, report: any, action: string): void {
        const key = this.getInstrumentKey(instrument);
        const keyStr = key.toString();
        if (!this.reportActions[keyStr]) {
            this.reportActions[keyStr] = {};
        }
        this.reportActions[keyStr][report.instrumentReportId] = action;
    }

    canIgnoreReports(instrument: any): boolean {
        const key = this.getInstrumentKey(instrument);
        const keyStr = key.toString();
        const actions = this.reportActions[keyStr];
        if (!actions) return false;

        const keepCount = Object.values(actions).filter(action => action === 'keep').length;
        const ignoreCount = Object.values(actions).filter(action => action === 'ignore').length;

        return keepCount === 1 && ignoreCount === Object.keys(actions).length - 1;
    }

    ignoreReports(instrument: any): Observable<void> {
        const key = this.getInstrumentKey(instrument);
        const keyStr = key.toString();
        this.validationErrors[keyStr] = null;
        this.successMessages[keyStr] = null;
        const actions = this.reportActions[keyStr];
        const keepReportId = Object.keys(actions).find(reportId => actions[+reportId] === 'keep');
        const ignoreReportIds = Object.keys(actions).filter(reportId => actions[+reportId] === 'ignore');

        if (!keepReportId || ignoreReportIds.length === 0) {
            this.validationErrors[keyStr] = 'Please select one report to keep and at least one report to ignore.';
            return of();
        }

        console.log(`Calling ignoreRawDataReports for instrument ${instrument.instrumentId}`);
        return this.companyService.ignoreRawDataReports(instrument.instrumentId, +keepReportId, ignoreReportIds.map(id => +id)).pipe(
            tap(() => {
                console.log(`Successfully ignored reports for instrument ${instrument.instrumentId}`);
                this.successMessages[keyStr] = 'Success';
            }),
            catchError(err => {
                console.error(`Failed to ignore reports for instrument ${instrument.instrumentId}: ${err}`);
                this.validationErrors[keyStr] = `Failed to ignore reports: ${err}`;
                this.errorCount++;
                return of();
            })
        );
    }

    onIgnoreReportsClick(instrument: any): void {
        console.log(`onIgnoreReportsClick called for instrument ${instrument.instrumentId}`);
        const subscription = this.ignoreReports(instrument).subscribe({
            next: () => console.log(`Ignored reports for instrument ${instrument.instrumentId}`),
            error: (err) => console.error(`Failed to ignore reports for instrument ${instrument.instrumentId}: ${err}`),
            complete: () => console.log(`Completed ignoring reports for instrument ${instrument.instrumentId}`)
        });
        this.subscriptions.add(subscription);
    }

    ignoreAllReports(): void {
        if (!this.data) return;

        this.isIgnoringReports = true;
        this.completedCount = 0;
        this.errorCount = 0;

        from(this.data.instrumentWithConflictingRawData).pipe(
            concatMap(instrument => this.canIgnoreReports(instrument) ? this.ignoreReports(instrument) : of()),
            catchError(err => {
                this.errorCount++;
                return of();
            })
        ).subscribe({
            next: () => this.completedCount++,
            error: (err) => console.error('Error processing instruments', err),
            complete: () => {
                this.isIgnoringReports = false;
                console.log('Finished processing all instruments');
            }
        });
    }
}
