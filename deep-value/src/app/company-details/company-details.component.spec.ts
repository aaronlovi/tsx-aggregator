import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { CompanyDetailsComponent } from './company-details.component';
import { CompanyService } from '../services/company.service';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';
import { CompanyDetails } from '../models/company.details';
import detailsFixture from '../../testing/company-full-detail-report.json';

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
    const details: CompanyDetails = { ...detailsFixture, overallScore: 7 };
    companyServiceSpy.getCompanyDetails.mockReturnValue(of(details));

    fixture.detectChanges();

    expect(companyServiceSpy.getCompanyDetails).toHaveBeenCalledWith('TSX', 'EXMP');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('7 / 15');
    expect(text).toContain('Example Corp');
  });
});
