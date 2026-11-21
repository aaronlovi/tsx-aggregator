import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { CompanyService } from './company.service';
import { AppConfigService } from '../app-config.service';
import { CompanyDetails } from '../models/company.details';
// Shared with the backend's CompanyDetailsApiContractTests, which checks that the API produces exactly this JSON.
import detailsFixture from '../../testing/company-full-detail-report.json';

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

        expect(details.overallScore).toBe(12);
        expect(details.maxPrice).toBe(24);
        expect(details.percentageUpside).toBe(-4);
        expect(details.doesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough).toBe(false);
        expect(details.doesPassCheck_ROE_FromOwnerEarnings_BigEnough).toBe(false);
        expect(details.doesPassCheck_DebtToEquitySmallEnough).toBe(true);
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
});
