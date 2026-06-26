import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';

import { CompanyListComponent } from './company-list.component';
import { CompanyService } from '../services/company.service';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';

describe('CompanyListComponent', () => {
  let component: CompanyListComponent;
  let fixture: ComponentFixture<CompanyListComponent>;

  beforeEach(() => {
    const companyServiceSpy = jasmine.createSpyObj('CompanyService', [
      'getCompanies', 'getBottomCompanies'
    ]);

    TestBed.configureTestingModule({
      // RelativeTimePipe is used in the template, so it must be declared here.
      declarations: [CompanyListComponent, RelativeTimePipe],
      imports: [RouterTestingModule],
      providers: [
        { provide: CompanyService, useValue: companyServiceSpy }
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

  describe('isStale', () => {
    const DAY_MS = 24 * 60 * 60 * 1000;
    const daysAgo = (n: number) => new Date(Date.now() - n * DAY_MS).toISOString();

    it('is false for null/undefined/empty', () => {
      expect(component.isStale(null)).toBeFalse();
      expect(component.isStale(undefined)).toBeFalse();
      expect(component.isStale('')).toBeFalse();
    });

    it('is false for an unparseable date', () => {
      expect(component.isStale('not-a-date')).toBeFalse();
    });

    it('is false for a recent date', () => {
      expect(component.isStale(daysAgo(5))).toBeFalse();
    });

    it('is true for a date older than 30 days', () => {
      expect(component.isStale(daysAgo(45))).toBeTrue();
    });

    it('handles the month boundary without drift (40 days ago is stale)', () => {
      // Guards against Date.setMonth overflow near month end.
      expect(component.isStale(daysAgo(40))).toBeTrue();
      expect(component.isStale(daysAgo(20))).toBeFalse();
    });
  });
});
