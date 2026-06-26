import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';

import { CompanyDetailsComponent } from './company-details.component';
import { CompanyService } from '../services/company.service';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';

describe('CompanyDetailsComponent', () => {
  let component: CompanyDetailsComponent;
  let fixture: ComponentFixture<CompanyDetailsComponent>;

  beforeEach(() => {
    const companyServiceSpy = jasmine.createSpyObj('CompanyService', ['getCompanyDetails']);

    TestBed.configureTestingModule({
      // RelativeTimePipe is used in the template, so it must be declared here.
      declarations: [CompanyDetailsComponent, RelativeTimePipe],
      imports: [RouterTestingModule],
      providers: [
        { provide: CompanyService, useValue: companyServiceSpy }
      ],
      // Template uses Material table/tooltip elements not declared here.
      schemas: [NO_ERRORS_SCHEMA]
    });
    fixture = TestBed.createComponent(CompanyDetailsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
