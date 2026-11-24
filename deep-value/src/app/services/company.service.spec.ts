import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { CompanyService } from './company.service';
import { AppConfigService } from '../app-config.service';
import { CompanyDetails } from '../models/company.details';
// Shared with the backend's CompanyDetailsApiContractTests, which checks that the API produces exactly this JSON.
import detailsFixture from '../../testing/company-full-detail-report.json';
import missingDataFixture from '../../testing/company-full-detail-report-missing-data.json';
import { CompanySummary } from '../models/company.summary';

describe('CompanyService', () => {
    let service: CompanyService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(),
                provideHttpClientTesting(),
                { provide: AppConfigService, useValue: { apiEndpoint: 'http://api.test', version: '' } },
            ],
        });
        service = TestBed.inject(CompanyService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    function fetchDetails(response: object): CompanyDetails {
        let result: CompanyDetails | undefined;
        service.getCompanyDetails('TSX', 'EXMP').subscribe(d => (result = d));
        http.expectOne('http://api.test/companies/TSX/EXMP').flush(response);
        expect(result).toBeDefined();
        return result!;
    }

    it('uses the score, checks and max price computed by the backend', () => {
        const details = fetchDetails(detailsFixture);

        expect(details.overallScore).toBe(13);
        expect(details.maxPrice).toBe(15);
        expect(details.percentageUpside).toBe(-40);
        expect(details.doesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough).toBe('Fail');
        expect(details.doesPassCheck_ROE_FromOwnerEarnings_BigEnough).toBe('Fail');
        expect(details.doesPassCheck_ROE_FromOwnerEarnings_Consistent).toBe('Pass');
        expect(details.roeOwnerEarningsYearsAbove10).toBe(4);
        expect(details.doesPassCheck_DebtToEquitySmallEnough).toBe('Pass');
        expect(details.notAvailableCount).toBe(0);
        expect(details.estimatedNextYearTotalReturnPercentage_FromOwnerEarnings).toBe(7.2);
    });

    it('passes every API field through unchanged', () => {
        const details = fetchDetails(detailsFixture);

        expect(details).toEqual(detailsFixture);
    });

    it('uses the backend score even if the raw inputs would score differently', () => {
        // The front end must not recompute: a different score from the API is shown as sent.
        const details = fetchDetails({ ...detailsFixture, overallScore: 3, maxPrice: 1.5 });

        expect(details.overallScore).toBe(3);
        expect(details.maxPrice).toBe(1.5);
    });

    it('normalises missing report dates to null', () => {
        const { mostRecentAnnualReportDate, mostRecentQuarterlyReportDate, lastUpdatedDate, ...rest } = detailsFixture;

        const details = fetchDetails(rest);

        expect(details.mostRecentAnnualReportDate).toBeNull();
        expect(details.mostRecentQuarterlyReportDate).toBeNull();
        expect(details.lastUpdatedDate).toBeNull();
    });

    it('keeps missing figures as null and n/a checks as NotAvailable', () => {
        const details = fetchDetails(missingDataFixture);

        expect(details).toEqual(missingDataFixture);
        expect(details.curTotalShareholdersEquity).toBeNull();
        expect(details.maxPrice).toBeNull();
        expect(details.doesPassCheck_DebtToEquitySmallEnough).toBe('NotAvailable');
        expect(details.didAdjustedRetainedEarningsIncrease).toBe('NotAvailable');
        expect(details.notAvailableCount).toBe(7);
        expect(details.overallScore).toBe(8);
    });

    it('maps list rows, keeping nulls and the backend percentage upside', () => {
        let result: CompanySummary[] | undefined;
        service.getCompanies().subscribe(d => (result = d));
        http.expectOne('http://api.test/companies').flush([
            {
                exchange: 'TSX', instrumentSymbol: 'AAA', companyName: 'A Corp', pricePerShare: 10, curMarketCap: 1000,
                estimatedNextYearTotalReturnPercentage_FromCashFlow: 6, estimatedNextYearTotalReturnPercentage_FromOwnerEarnings: null,
                overallScore: 13, notAvailableCount: 2, maxPrice: null, percentageUpside: null,
                returnOnEquity_FromCashFlow: 0.12, returnOnEquity_FromOwnerEarnings: null,
                mostRecentAnnualReportDate: null, mostRecentQuarterlyReportDate: null, lastUpdatedDate: null,
            },
        ]);

        expect(result).toHaveLength(1);
        const row = result![0];
        expect(row.overallScore).toBe(13);
        expect(row.notAvailableCount).toBe(2);
        expect(row.estimatedNextYearTotalReturnPercentageFromCashFlow).toBe(6);
        expect(row.estimatedNextYearTotalReturnPercentageOwnerEarnings).toBeNull();
        expect(row.maxPrice).toBeNull();
        expect(row.percentageUpside).toBeNull();
        expect(row.returnOnEquity_FromOwnerEarnings).toBeNull();
    });
});
