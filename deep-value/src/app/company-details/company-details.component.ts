import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { CompanyService } from '../services/company.service';
import { CompanyDetails } from '../models/company.details';

@Component({
    selector: 'app-company-details',
    templateUrl: './company-details.component.html',
    styleUrls: ['./company-details.component.scss'],
    standalone: false
})
export class CompanyDetailsComponent implements OnInit, OnDestroy {
    exchange: string;
    instrumentSymbol: string;
    companyDetails?: CompanyDetails;
    loading: boolean;
    errorMsg: string;
    now: Date = new Date();

    private routeSub: Subscription | null = null;
    private timerInterval: ReturnType<typeof setInterval> | null = null;

    constructor(private companyService: CompanyService, private route: ActivatedRoute) {
        this.exchange = '';
        this.instrumentSymbol = '';
        this.companyDetails = undefined;
        this.loading = false;
        this.errorMsg = '';
    }

    ngOnInit(): void {
        this.routeSub = this.route.paramMap.subscribe(params => {
            this.exchange = params.get('exchange') || '';
            this.instrumentSymbol = params.get('instrumentSymbol') || '';

            console.log(`Exchange = ${this.exchange}, Instrument Symbol: ${this.instrumentSymbol}`);

            if (this.isInputsValid()) {
                this.getCompanyDetails();
            } else {
                this.errorMsg = 'Invalid exchange or instrument symbol';
            }
        });

        // Keep the "Last Scraped" relative time ticking, like the list view.
        this.timerInterval = setInterval(() => {
            this.now = new Date();
        }, 1000);
    }

    ngOnDestroy(): void {
        if (this.routeSub) {
            this.routeSub.unsubscribe();
        }
        if (this.timerInterval) {
            clearInterval(this.timerInterval);
        }
    }

    isInputsValid(): boolean {
        return !!this.exchange && !!this.instrumentSymbol;
    }
      
    getCompanyDetails(): void {
        this.loading = true;
        this.errorMsg = '';
        this.companyDetails = undefined;

        this.companyService.getCompanyDetails(this.exchange, this.instrumentSymbol)
            .subscribe((data: CompanyDetails) => {
                this.companyDetails = data;
                this.loading = false;
            },
            (error: any) => {
                if (error?.status === 404) {
                    this.errorMsg = `No data available yet for ${this.instrumentSymbol} on ${this.exchange}`;
                } else {
                    this.errorMsg = 'An error occurred while fetching company data';
                }
                this.loading = false;
            }
        );
    }
}
