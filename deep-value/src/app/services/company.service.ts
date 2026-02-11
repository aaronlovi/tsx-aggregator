import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, map, tap, throwError } from 'rxjs';
import { AppConfigService } from '../app-config.service';
import { CompanySummary } from '../models/company.summary';
import { CompanyDetails } from '../models/company.details';
import { DashboardAggregates, DashboardStats } from '../models/dashboard-stats';
import { InstrumentsWithConflictingRawData } from '../models/instruments_with_conflicting_raw_data';
import { PagingData } from '../models/paging_data';
import { InstrumentRawReportData } from '../models/instrument_raw_report_data';
import { InstrumentWithConflictingRawData } from '../models/instrument_with_conflicting_raw_data';

@Injectable({
    providedIn: 'root'
})
export class CompanyService {

    constructor(private http: HttpClient, private config: AppConfigService) { }

    getCompanies(): Observable<CompanySummary[]> {
        return this.http.get<any[]>(`${this.config.apiEndpoint}/companies`).pipe(
            map(data => {
                return data.map(item => new CompanySummary(
                    item.exchange,
                    item.instrumentSymbol,
                    item.companyName,
                    item.pricePerShare,
                    item.curMarketCap,
                    item.estimatedNextYearTotalReturnPercentage_FromCashFlow,
                    item.estimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
                    item.overallScore,
                    item.maxPrice
                ));
            })
        );
    }

    getBottomCompanies(): Observable<CompanySummary[]> {
        return this.http.get<any[]>(`${this.config.apiEndpoint}/companies/bottom`).pipe(
            map(data => {
                return data.map(item => new CompanySummary(
                    item.exchange,
                    item.instrumentSymbol,
                    item.companyName,
                    item.pricePerShare,
                    item.curMarketCap,
                    item.estimatedNextYearTotalReturnPercentage_FromCashFlow,
                    item.estimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
                    item.overallScore,
                    item.maxPrice
                ));
            })
        );
    }

    getAllCompanies(pageNumber: number, pageSize: number): Observable<any> {
        return this.http.get<any>(
            `${this.config.apiEndpoint}/companies/all?pageNumber=${pageNumber}&pageSize=${pageSize}`
        );
    }

    getCompanyDetails(exchange: string, instrumentSymbol: string): Observable<CompanyDetails> {
        return this.http.get<any>(`${this.config.apiEndpoint}/companies/${exchange}/${instrumentSymbol}`).pipe(
            map(data => {
                return new CompanyDetails(
                    data.exchange,
                    data.companySymbol,
                    data.instrumentSymbol,
                    data.companyName,
                    data.instrumentName,
                    data.pricePerShare,
                    data.curLongTermDebt,
                    data.curTotalShareholdersEquity,
                    data.curBookValue,
                    data.curNumShares,
                    data.averageNetCashFlow,
                    data.averageOwnerEarnings,
                    data.curDividendsPaid,
                    data.curAdjustedRetainedEarnings,
                    data.oldestRetainedEarnings,
                    data.numAnnualProcessedCashFlowReports);
            })
        );
    }

    getDashboardStats(): Observable<DashboardStats> {
        return this.http.get<any>(`${this.config.apiEndpoint}/companies/dashboard`).pipe(
            map(data => new DashboardStats(
                data.totalActiveInstruments,
                data.totalObsoletedInstruments,
                data.instrumentsWithProcessedReports,
                data.instrumentsWithoutProcessedReports,
                data.mostRecentRawIngestion,
                data.mostRecentAggregation,
                data.unprocessedEventCount,
                data.manualReviewCount,
                data.rawReportCounts,
                data.nextFetchDirectoryTime,
                data.nextFetchInstrumentDataTime,
                data.nextFetchQuotesTime
            ))
        );
    }

    getDashboardAggregates(): Observable<DashboardAggregates> {
        return this.http.get<any>(`${this.config.apiEndpoint}/companies/dashboard/aggregates`).pipe(
            map(data => new DashboardAggregates(
                data.totalCompanies,
                data.companiesWithPriceData,
                data.companiesWithoutPriceData,
                data.companiesPassingAllChecks,
                data.averageEstimatedReturn_FromCashFlow,
                data.averageEstimatedReturn_FromOwnerEarnings,
                data.medianEstimatedReturn_FromCashFlow,
                data.medianEstimatedReturn_FromOwnerEarnings,
                data.totalMarketCap,
                data.scoreDistribution
            ))
        );
    }

    getMissingDataCompanies(exchange: string, pageNumber: number, pageSize: number): Observable<any> {
        return this.http.get<any>(
            `${this.config.apiEndpoint}/companies/missing_data?exchange=${exchange}&pageNumber=${pageNumber}&pageSize=${pageSize}`
        );
    }

    getUpdatedRawDataReports(exchange: string, pageNumber: number, pageSize: number): Observable<any> {
        return this.http.get<any>(`${this.config.apiEndpoint}/companies/updated_raw_data_reports?exchange=${exchange}&pageNumber=${pageNumber}&pageSize=${pageSize}`).pipe(
            map(data => {
                const pagingData = new PagingData(data.pagingData.totalItems, data.pagingData.pageNumber, data.pagingData.pageSize);
                const instruments = data.instrumentWithConflictingRawData.map((instrument: any) => {
                    const conflictingRawReports = instrument.conflictingRawReports.map((report: any) =>
                        new InstrumentRawReportData(
                            report.instrumentReportId,
                            new Date(report.reportCreatedDate),
                            report.isCurrent,
                            report.checkManually,
                            report.ignoreReport,
                            report.reportJson
                        )
                    );
                    return new InstrumentWithConflictingRawData(
                        instrument.instrumentId,
                        instrument.exchange,
                        instrument.companySymbol,
                        instrument.instrumentSymbol,
                        instrument.companyName,
                        instrument.instrumentName,
                        instrument.reportType,
                        instrument.reportPeriodType,
                        new Date(instrument.reportDate),
                        conflictingRawReports
                    );
                });
                return new InstrumentsWithConflictingRawData(pagingData, instruments);
            })
        );
    }

    setPriorityCompanies(symbols: string[]): Observable<any> {
        const url = `${this.config.apiEndpoint}/companies/priority`;
        const headers = new HttpHeaders({ 'Content-Type': 'application/json' });
        return this.http.post<void>(url, symbols, { headers }).pipe(
            catchError(this.handleError)
        );
    }

    getPriorityCompanies(): Observable<string[]> {
        return this.http.get<string[]>(`${this.config.apiEndpoint}/companies/priority`);
    }

    ignoreRawDataReports(instrumentId: number, instrumentReportIdToKeep: number, instrumentReportIdsToIgnore: number[]): Observable<any> {
        const url = `${this.config.apiEndpoint}/companies/ignore_raw_report/${instrumentId}/${instrumentReportIdToKeep}`;
        const headers = new HttpHeaders({ 'Content-Type': 'application/json' });

        return this.http.post<void>(url, instrumentReportIdsToIgnore, { headers }).pipe(
            tap(() => console.log(`POST request to ${url} successful`)),
            catchError(this.handleError)
        );
    }

    private handleError(error: HttpErrorResponse): Observable<never> {
        let errorMessage = 'Unknown error';
        if (error.error instanceof ErrorEvent) {
            errorMessage = `A client-side or network error occurred: ${error.error.message}`;
        } else {
            // Backend returned an unsuccessful response code
            errorMessage = `Backend returned code ${error.status}, body was: ${error.error.error}`;
        }
        console.error(errorMessage);
        return throwError(() => new Error(errorMessage));
    }
}
