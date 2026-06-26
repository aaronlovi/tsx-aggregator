import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';

import { QuickSearchComponent } from './quick-search.component';
import { CompanySearchService } from '../services/company-search.service';

describe('QuickSearchComponent', () => {
  let component: QuickSearchComponent;
  let fixture: ComponentFixture<QuickSearchComponent>;

  beforeEach(() => {
    const searchServiceSpy = jasmine.createSpyObj('CompanySearchService', ['quickSearch']);

    TestBed.configureTestingModule({
      declarations: [QuickSearchComponent],
      // The template's `#auto="matAutocomplete"` reference needs the real
      // Material modules; they can't be stubbed with NO_ERRORS_SCHEMA.
      imports: [
        RouterTestingModule, FormsModule, NoopAnimationsModule,
        MatAutocompleteModule, MatFormFieldModule, MatInputModule, MatIconModule
      ],
      providers: [
        { provide: CompanySearchService, useValue: searchServiceSpy }
      ]
    });
    fixture = TestBed.createComponent(QuickSearchComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
