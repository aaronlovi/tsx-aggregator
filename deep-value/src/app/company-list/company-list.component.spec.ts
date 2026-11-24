import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';

import { CompanyListComponent } from './company-list.component';
import { CompanyService } from '../services/company.service';
import { CompanySummary } from '../models/company.summary';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';

describe('CompanyListComponent', () => {
  let component: CompanyListComponent;
  let fixture: ComponentFixture<CompanyListComponent>;
  let companyServiceSpy: { getCompanies: ReturnType<typeof vi.fn>, getBottomCompanies: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    companyServiceSpy = { getCompanies: vi.fn(), getBottomCompanies: vi.fn() };

    TestBed.configureTestingModule({
      // RelativeTimePipe is used in the template, so it must be declared here.
      declarations: [CompanyListComponent, RelativeTimePipe],
      imports: [RouterTestingModule, MatTableModule],
      providers: [
        { provide: CompanyService, useValue: companyServiceSpy },
        // Toronto, whatever the machine's time zone
        { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { timezone: '-0500' } }
      ],
      // Template uses Material table/tooltip elements not declared here.
      schemas: [NO_ERRORS_SCHEMA]
    });
    fixture = TestBed.createComponent(CompanyListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows report dates as they are, and the scrape time in local time', () => {
    // Report dates are midnight UTC; the scrape time is a moment.
    companyServiceSpy.getCompanies.mockReturnValue(of([new CompanySummary({
      exchange: 'TSX', instrumentSymbol: 'EXMP', companyName: 'Example Corp', pricePerShare: 25, overallScore: 14,
      mostRecentAnnualReportDate: '2025-12-31T00:00:00+00:00',
      mostRecentQuarterlyReportDate: '2026-06-30T00:00:00+00:00',
      lastUpdatedDate: '2026-08-26T02:02:35+00:00'
    })]));

    fixture.detectChanges();

    const tooltips = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('td'))
      .map(cell => (cell as unknown as { matTooltip?: string }).matTooltip ?? '')
      .filter(tooltip => tooltip.includes('Annual:'));
    expect(tooltips).toHaveLength(1);
    expect(tooltips[0]).toContain('Annual: 2025-12-31');
    expect(tooltips[0]).toContain('Quarterly: 2026-06-30');
    expect(tooltips[0]).toContain('Scraped: 2026-08-25 21:02');
    fixture.destroy();
  });

  describe('isStale', () => {
    const DAY_MS = 24 * 60 * 60 * 1000;
    const daysAgo = (n: number) => new Date(Date.now() - n * DAY_MS).toISOString();

    it('is false for null/undefined/empty', () => {
      expect(component.isStale(null)).toBe(false);
      expect(component.isStale(undefined)).toBe(false);
      expect(component.isStale('')).toBe(false);
    });

    it('is false for an unparseable date', () => {
      expect(component.isStale('not-a-date')).toBe(false);
    });

    it('is false for a recent date', () => {
      expect(component.isStale(daysAgo(5))).toBe(false);
    });

    it('is true for a date older than 30 days', () => {
      expect(component.isStale(daysAgo(45))).toBe(true);
    });

    it('handles the month boundary without drift (40 days ago is stale)', () => {
      // Guards against Date.setMonth overflow near month end.
      expect(component.isStale(daysAgo(40))).toBe(true);
      expect(component.isStale(daysAgo(20))).toBe(false);
    });
  });
});
