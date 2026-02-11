import { Component, OnDestroy, OnInit } from '@angular/core';
import { DashboardAggregates, DashboardStats, ScoreDistributionItem } from '../models/dashboard-stats';
import { CompanyService } from '../services/company.service';

@Component({
    selector: 'app-dashboard',
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit, OnDestroy {
    loading: boolean = false;
    errorMsg: string = '';
    stats: DashboardStats | null = null;

    aggregatesLoading: boolean = false;
    aggregatesErrorMsg: string = '';
    aggregates: DashboardAggregates | null = null;

    lastUpdated: Date | null = null;
    now: Date = new Date();
    private timerInterval: ReturnType<typeof setInterval> | null = null;

    constructor(private companyService: CompanyService) { }

    ngOnInit() {
        this.loadStats();
        this.loadAggregates();
        this.timerInterval = setInterval(() => this.now = new Date(), 1000);
    }

    ngOnDestroy() {
        if (this.timerInterval) {
            clearInterval(this.timerInterval);
        }
    }

    refreshData() {
        this.loadStats();
        this.loadAggregates();
    }

    private loadStats() {
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getDashboardStats().subscribe(
            (data: DashboardStats) => {
                this.stats = data;
                this.loading = false;
                this.lastUpdated = new Date();
            },
            (error: any) => {
                this.errorMsg = 'An error occurred while fetching dashboard stats';
                this.loading = false;
            }
        );
    }

    get scoreDistributionLeft(): ScoreDistributionItem[] {
        if (!this.aggregates) return [];
        const items = this.aggregates.scoreDistribution;
        return items.slice(0, Math.ceil(items.length / 2));
    }

    get scoreDistributionRight(): ScoreDistributionItem[] {
        if (!this.aggregates) return [];
        const items = this.aggregates.scoreDistribution;
        return items.slice(Math.ceil(items.length / 2));
    }

    private loadAggregates() {
        this.aggregatesLoading = true;
        this.aggregatesErrorMsg = '';

        this.companyService.getDashboardAggregates().subscribe(
            (data: DashboardAggregates) => {
                this.aggregates = data;
                this.aggregatesLoading = false;
            },
            (error: any) => {
                this.aggregatesErrorMsg = 'An error occurred while fetching company aggregates';
                this.aggregatesLoading = false;
            }
        );
    }
}
