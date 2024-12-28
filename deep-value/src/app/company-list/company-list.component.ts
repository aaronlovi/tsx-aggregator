import { Component } from '@angular/core';
import { CompanyService } from '../services/company.service';
import { CompanySummary } from '../models/company.summary';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-company-list',
    templateUrl: './company-list.component.html',
    styleUrls: ['./company-list.component.scss']
})
export class CompanyListComponent {
    displayedColumns: string[] = [
        'instrumentSymbol',
        'companyName',
        'pricePerShare',
        'marketCap',
        'estimatedNextYearTotalReturnPercentageFromCashFlow',
        'estimatedNextYearTotalReturnPercentageOwnerEarnings',
        'overallScore'
    ];
    companies: CompanySummary[];
    loading: boolean;
    errorMsg: string;

    constructor(public textService: TextService, private companyService: CompanyService) {
        this.companies = [];
        this.loading = false;
        this.errorMsg = '';
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
}
