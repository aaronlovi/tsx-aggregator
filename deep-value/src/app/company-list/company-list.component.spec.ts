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
});
