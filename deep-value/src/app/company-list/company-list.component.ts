import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { CompanySummary } from '../models/company.summary';
import { CompanyService } from '../services/company.service';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-company-list',
    templateUrl: './company-list.component.html',
    styleUrls: ['./company-list.component.scss']
})
export class CompanyListComponent implements OnInit, OnDestroy {
    displayedColumns: string[] = [
        'instrumentSymbol',
        'companyName',
        'pricePerShare',
        'maxPrice',
        'marketCap',
        'estimatedNextYearTotalReturnPercentageFromCashFlow',
        'estimatedNextYearTotalReturnPercentageOwnerEarnings',
        'overallScore'
    ];
    companies: CompanySummary[];
    loading: boolean;
    errorMsg: string;
    pageTitle: string = '';
    private routeSub: Subscription | null = null;

    constructor(
        public textService: TextService,
        private companyService: CompanyService,
        private route: ActivatedRoute
    ) {
        this.companies = [];
        this.loading = false;
        this.errorMsg = '';
    }

    ngOnInit() {
        this.routeSub = this.route.data.subscribe(data => {
            if (data['mode'] === 'bottom') {
                this.pageTitle = 'Bottom 30 Companies';
                this.loadBottomCompanies();
            } else {
                this.pageTitle = 'Top 30 Companies';
                this.loadCompanies();
            }
        });
    }

    ngOnDestroy() {
        if (this.routeSub) {
            this.routeSub.unsubscribe();
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
            },
            (error: any) => {
                this.errorMsg = 'An error occurred while fetching companies data';
                this.loading = false;
            }
        );
    }
}
