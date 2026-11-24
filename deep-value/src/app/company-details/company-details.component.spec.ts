import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS } from '@angular/common';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { CompanyDetailsComponent } from './company-details.component';
import { CompanyService } from '../services/company.service';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';
import { CompanyDetails } from '../models/company.details';
import detailsFixture from '../../testing/company-full-detail-report.json';
import missingDataFixture from '../../testing/company-full-detail-report-missing-data.json';

describe('CompanyDetailsComponent', () => {
  let component: CompanyDetailsComponent;
  let fixture: ComponentFixture<CompanyDetailsComponent>;
  let companyServiceSpy: { getCompanyDetails: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    companyServiceSpy = { getCompanyDetails: vi.fn() };

    TestBed.configureTestingModule({
      // RelativeTimePipe is used in the template, so it must be declared here.
      declarations: [CompanyDetailsComponent, RelativeTimePipe],
      providers: [
        { provide: CompanyService, useValue: companyServiceSpy },
        // Toronto, whatever the machine's time zone
        { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { timezone: '-0500' } },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ exchange: 'TSX', instrumentSymbol: 'EXMP' })) } },
      ],
      // Template uses Material table/tooltip elements not declared here.
      schemas: [NO_ERRORS_SCHEMA]
    });
    fixture = TestBed.createComponent(CompanyDetailsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => fixture.destroy());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows the score sent by the backend', () => {
    // A score the raw inputs would not produce, to prove the page does not recompute it.
    const details = { ...detailsFixture, overallScore: 7 } as CompanyDetails;
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(details));

    fixture.detectChanges();

    expect(companyServiceSpy.getCompanyDetails).toHaveBeenCalledWith('TSX', 'EXMP');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('7 / 16');
    expect(text).toContain('Example Corp');
  });

  it('shows the ROE consistency check with how many of the last years were over 10%', () => {
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(detailsFixture as CompanyDetails));

    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('ROE from Owner Earnings Consistent?');
    expect(root.querySelector('.roe-years')?.textContent).toContain('(4 of 5)');
  });

  it('shows the latest year under the averages', () => {
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(detailsFixture as CompanyDetails));

    fixture.detectChanges();

    const latest = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.latest-year'))
      .map(cell => cell.textContent?.trim());
    expect(latest).toEqual(['latest $20,000,000', 'latest $30,000,000', 'latest $95,000,000']);
  });

  it('shows n/a for a latest year that is missing', () => {
    const details = { ...detailsFixture, curDividendsPaid: null, latestNetCashFlow: null, latestOwnerEarnings: null } as CompanyDetails;
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(details));

    fixture.detectChanges();

    const latest = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.latest-year'))
      .map(cell => cell.textContent?.trim());
    expect(latest).toEqual(['latest n/a', 'latest n/a', 'latest n/a']);
  });

  it('shows a latest year of 0 or less as a figure', () => {
    const details = { ...detailsFixture, latestNetCashFlow: 0, latestOwnerEarnings: -5000000 } as CompanyDetails;
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(details));

    fixture.detectChanges();

    const latest = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.latest-year'))
      .map(cell => cell.textContent?.trim());
    expect(latest).toEqual(['latest $20,000,000', 'latest $0', 'latest -$5,000,000']);
  });

  it('shows report dates as they are, not a day early in a western time zone', () => {
    // Report dates are midnight UTC.
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(detailsFixture as CompanyDetails));

    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('2025-12-31');
    expect(text).toContain('2026-06-30');
  });

  it('shows Yes and No for checks with data, and no n/a count', () => {
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(detailsFixture as CompanyDetails));

    fixture.detectChanges();

    const cells = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.data-table__data--score'))
      .map(cell => cell.textContent?.trim());
    expect(cells).toContain('Yes');
    expect(cells).toContain('No');
    expect(cells).not.toContain('n/a');
    expect((fixture.nativeElement as HTMLElement).querySelector('.na-count')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.estimated')).toBeNull();
  });

  it('shows n/a for missing figures and checks, and the n/a count next to the score', () => {
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(missingDataFixture as CompanyDetails));

    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const naCells = Array.from(root.querySelectorAll('.data-table__data--score.na'));
    expect(naCells).toHaveLength(7);
    expect(naCells.every(cell => cell.textContent?.trim() === 'n/a')).toBe(true);
    expect(root.querySelector('.na-count')?.textContent).toContain('(7 n/a)');
    expect(root.textContent).toContain('8 / 16');
    // e.g. Max Buy Price and Total Shareholders Equity
    const numericCells = Array.from(root.querySelectorAll('.data-table__data--numeric')).map(cell => cell.textContent?.trim());
    expect(numericCells.filter(text => text === 'n/a').length).toBeGreaterThanOrEqual(10);
    // Current retained earnings are estimated in this fixture; the oldest are missing.
    expect(Array.from(root.querySelectorAll('.estimated')).map(e => e.textContent?.trim())).toEqual(['(est.)']);
  });

  it('maps check results to labels and classes', () => {
    expect(component.checkLabel('Pass')).toBe('Yes');
    expect(component.checkLabel('Fail')).toBe('No');
    expect(component.checkLabel('NotAvailable')).toBe('n/a');
    expect(component.checkClass('Fail')).toEqual({ no: true, na: false });
    expect(component.checkClass('NotAvailable')).toEqual({ no: false, na: true });
  });
});
