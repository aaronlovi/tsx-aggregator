import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { CompanyService } from '../services/company.service';
import { DashboardAggregates, DashboardStats } from '../models/dashboard-stats';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';

describe('DashboardComponent', () => {
    let component: DashboardComponent;
    let fixture: ComponentFixture<DashboardComponent>;
    let mockCompanyService: jasmine.SpyObj<CompanyService>;

    const mockStats = new DashboardStats(
        1000, 50, 800, 200,
        '2025-01-15T10:00:00Z', '2025-01-15T09:00:00Z',
        15, 7,
        [{ reportType: 1, reportTypeName: 'Cash Flow', count: 500 }],
        '2025-01-15T12:00:00Z', '2025-01-15T13:00:00Z', '2025-01-15T14:00:00Z'
    );

    const mockAggregates = new DashboardAggregates(
        500, 450, 50, 5, 12.34, 8.76,
        [{ score: 13, count: 5 }, { score: 12, count: 10 }]
    );

    beforeEach(() => {
        mockCompanyService = jasmine.createSpyObj('CompanyService', ['getDashboardStats', 'getDashboardAggregates']);
        mockCompanyService.getDashboardStats.and.returnValue(of(mockStats));
        mockCompanyService.getDashboardAggregates.and.returnValue(of(mockAggregates));

        TestBed.configureTestingModule({
            declarations: [DashboardComponent],
            imports: [CommonModule, MatCardModule, MatIconModule, MatButtonModule],
            providers: [
                { provide: CompanyService, useValue: mockCompanyService }
            ]
        });
        fixture = TestBed.createComponent(DashboardComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('should load stats and aggregates on init', () => {
        fixture.detectChanges();
        expect(mockCompanyService.getDashboardStats).toHaveBeenCalled();
        expect(mockCompanyService.getDashboardAggregates).toHaveBeenCalled();
        expect(component.stats).toBeTruthy();
        expect(component.stats!.totalActiveInstruments).toBe(1000);
        expect(component.aggregates).toBeTruthy();
        expect(component.aggregates!.totalCompanies).toBe(500);
        expect(component.loading).toBeFalse();
        expect(component.aggregatesLoading).toBeFalse();
    });

    it('should set errorMsg on stats failure', () => {
        mockCompanyService.getDashboardStats.and.returnValue(throwError(() => new Error('fail')));
        fixture.detectChanges();
        expect(component.errorMsg).toBeTruthy();
        expect(component.loading).toBeFalse();
    });

    it('should set aggregatesErrorMsg on aggregates failure', () => {
        mockCompanyService.getDashboardAggregates.and.returnValue(throwError(() => new Error('fail')));
        fixture.detectChanges();
        expect(component.aggregatesErrorMsg).toBeTruthy();
        expect(component.aggregatesLoading).toBeFalse();
    });

    it('should reload both on refreshData', () => {
        fixture.detectChanges();
        mockCompanyService.getDashboardStats.calls.reset();
        mockCompanyService.getDashboardAggregates.calls.reset();
        component.refreshData();
        expect(mockCompanyService.getDashboardStats).toHaveBeenCalledTimes(1);
        expect(mockCompanyService.getDashboardAggregates).toHaveBeenCalledTimes(1);
    });
});
