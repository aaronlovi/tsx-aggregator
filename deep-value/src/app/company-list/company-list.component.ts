import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { CompanySummary } from '../models/company.summary';
import { CompanyService } from '../services/company.service';

@Component({
    selector: 'app-company-list',
    templateUrl: './company-list.component.html',
    styleUrls: ['./company-list.component.scss'],
    standalone: false
})
export class CompanyListComponent implements OnInit, OnDestroy {
    displayedColumns: string[] = [
        'instrumentSymbol',
        'companyName',
        'pricePerShare',
        'maxPrice',
        'percentageUpside',
        'marketCap',
        'estimatedNextYearTotalReturnPercentageFromCashFlow',
        'estimatedNextYearTotalReturnPercentageOwnerEarnings',
        'overallScore'
    ];
    companies: CompanySummary[];
    loading: boolean;
    errorMsg: string;
    pageTitle: string = '';
    lastUpdated: Date | null = null;
    now: Date = new Date();
    nextAutoRefreshTime: Date | null = null;
    private mode: string = 'top';
    private routeSub: Subscription | null = null;
    private timerInterval: ReturnType<typeof setInterval> | null = null;
    private static readonly AUTO_REFRESH_MS = 10 * 60 * 1000;

    constructor(
        private companyService: CompanyService,
        private route: ActivatedRoute
    ) {
        this.companies = [];
        this.loading = false;
        this.errorMsg = '';
    }

    ngOnInit() {
        this.routeSub = this.route.data.subscribe(data => {
            this.mode = data['mode'] || 'top';
            if (this.mode === 'bottom') {
                this.pageTitle = 'Bottom 30 Companies';
                this.loadBottomCompanies();
            } else {
                this.pageTitle = 'Top 30 Companies';
                this.loadCompanies();
            }
        });
        this.timerInterval = setInterval(() => {
            this.now = new Date();
            this.autoRefreshIfScheduleElapsed();
        }, 1000);
    }

    ngOnDestroy() {
        if (this.routeSub) {
            this.routeSub.unsubscribe();
        }
        if (this.timerInterval) {
            clearInterval(this.timerInterval);
        }
    }

    refreshData() {
        if (this.mode === 'bottom') {
            this.loadBottomCompanies();
        } else {
            this.loadCompanies();
        }
    }

    private scheduleNextAutoRefresh() {
        this.nextAutoRefreshTime = new Date(Date.now() + CompanyListComponent.AUTO_REFRESH_MS);
    }

    private autoRefreshIfScheduleElapsed() {
        if (!this.nextAutoRefreshTime || this.loading) return;
        if (this.now.getTime() >= this.nextAutoRefreshTime.getTime()) {
            this.refreshData();
        }
    }

    loadCompanies() {
        this.companies = [];
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getCompanies().subscribe(
            (data: Object) => {
                if (Array.isArray(data)) {
                    this.companies = data as CompanySummary[];
                } else {
                    console.error('Invalid data format received from API. Expected an array.');
                }
                this.loading = false;
                this.lastUpdated = new Date();
                this.scheduleNextAutoRefresh();
            },
            (error: any) => {
                this.errorMsg = 'An error occurred while fetching companies data';
                this.loading = false;
            }
        );
    }

    loadBottomCompanies() {
        this.companies = [];
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getBottomCompanies().subscribe(
            (data: Object) => {
                if (Array.isArray(data)) {
                    this.companies = data as CompanySummary[];
                } else {
                    console.error('Invalid data format received from API. Expected an array.');
                }
                this.loading = false;
                this.lastUpdated = new Date();
                this.scheduleNextAutoRefresh();
            },
            (error: any) => {
                this.errorMsg = 'An error occurred while fetching companies data';
                this.loading = false;
            }
        );
    }
}
