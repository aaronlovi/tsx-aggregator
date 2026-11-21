import type { Mocked } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { CompanyService } from '../services/company.service';
import { DashboardAggregates, DashboardStats } from '../models/dashboard-stats';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';
import { CompactCurrencyPipe } from '../pipes/compact-currency.pipe';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';

describe('DashboardComponent', () => {
    let component: DashboardComponent;
    let fixture: ComponentFixture<DashboardComponent>;
    let mockCompanyService: Mocked<CompanyService>;

    const mockStats = new DashboardStats(
        1000, 50, 800, 200,
        '2025-01-15T10:00:00Z', '2025-01-15T09:00:00Z',
        15,
        [{ reportType: 1, reportTypeName: 'Cash Flow', count: 500 }],
        '2025-01-15T12:00:00Z', '2025-01-15T13:00:00Z', '2025-01-15T14:00:00Z', '2025-01-15T15:00:00Z'
    );

    const mockAggregates = new DashboardAggregates(
        500, 450, 50, 5, 12.34, 8.76, 10.50, 7.25, 1234567890,
        [{ score: 15, count: 5 }, { score: 14, count: 10 }],
        [{
            score: 15, count: 5, sumMarketCap: 1000, meanMarketCap: 200, medianMarketCap: 180,
            meanReturnFromCashFlow: 12.3, medianReturnFromCashFlow: 11.0,
            meanReturnFromOwnerEarnings: 8.5, medianReturnFromOwnerEarnings: 8.0
        }]
    );

    beforeEach(() => {
        mockCompanyService = { getDashboardStats: vi.fn(), getDashboardAggregates: vi.fn() } as unknown as Mocked<CompanyService>;
        mockCompanyService.getDashboardStats.mockReturnValue(of(mockStats));
        mockCompanyService.getDashboardAggregates.mockReturnValue(of(mockAggregates));

        TestBed.configureTestingModule({
            // Template uses the compactCurrency and relativeTime custom pipes.
            declarations: [DashboardComponent, RelativeTimePipe, CompactCurrencyPipe],
            imports: [CommonModule, MatCardModule, MatIconModule, MatButtonModule],
            providers: [
                { provide: CompanyService, useValue: mockCompanyService }
            ],
            schemas: [NO_ERRORS_SCHEMA]
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
        expect(component.loading).toBe(false);
        expect(component.aggregatesLoading).toBe(false);
    });

    it('should set errorMsg on stats failure', () => {
        mockCompanyService.getDashboardStats.mockReturnValue(throwError(() => new Error('fail')));
        fixture.detectChanges();
        expect(component.errorMsg).toBeTruthy();
        expect(component.loading).toBe(false);
    });

    it('should set aggregatesErrorMsg on aggregates failure', () => {
        mockCompanyService.getDashboardAggregates.mockReturnValue(throwError(() => new Error('fail')));
        fixture.detectChanges();
        expect(component.aggregatesErrorMsg).toBeTruthy();
        expect(component.aggregatesLoading).toBe(false);
    });

    it('should reload both on refreshData', () => {
        fixture.detectChanges();
        mockCompanyService.getDashboardStats.mockClear();
        mockCompanyService.getDashboardAggregates.mockClear();
        component.refreshData();
        expect(mockCompanyService.getDashboardStats).toHaveBeenCalledTimes(1);
        expect(mockCompanyService.getDashboardAggregates).toHaveBeenCalledTimes(1);
    });
});
