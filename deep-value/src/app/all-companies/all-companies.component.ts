import { Component, OnInit } from '@angular/core';
import { CompanySummary } from '../models/company.summary';
import { PagingData } from '../models/paging_data';
import { CompanyService } from '../services/company.service';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-all-companies',
    templateUrl: './all-companies.component.html',
    styleUrls: ['./all-companies.component.scss']
})
export class AllCompaniesComponent implements OnInit {
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
    companies: CompanySummary[] = [];
    loading: boolean = false;
    errorMsg: string = '';
    pagingData: PagingData | null = null;
    pageSize: number = 30;

    constructor(
        public textService: TextService,
        private companyService: CompanyService
    ) { }

    ngOnInit() {
        this.loadPage(1);
    }

    loadPage(pageNumber: number) {
        this.companies = [];
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getAllCompanies(pageNumber, this.pageSize).subscribe({
            next: (data: any) => {
                this.pagingData = new PagingData(
                    data.pagingData.totalItems,
                    data.pagingData.pageNumber,
                    data.pagingData.pageSize
                );
                this.companies = data.companies.map((item: any) => new CompanySummary(
                    item.exchange,
                    item.instrumentSymbol,
                    item.companyName,
                    item.pricePerShare,
                    item.curMarketCap,
                    item.estimatedNextYearTotalReturnPercentage_FromCashFlow,
                    item.estimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
                    item.overallScore,
                    item.maxPrice
                ));
                this.loading = false;
            },
            error: (error: any) => {
                this.errorMsg = 'An error occurred while fetching companies data';
                this.loading = false;
            }
        });
    }

    goToFirstPage() {
        this.loadPage(1);
    }

    goToPreviousPage() {
        if (!this.pagingData || this.pagingData.pageNumber <= 1) return;
        this.loadPage(this.pagingData.pageNumber - 1);
    }

    goToNextPage() {
        if (!this.pagingData || this.pagingData.pageNumber >= this.pagingData.numPages) return;
        this.loadPage(this.pagingData.pageNumber + 1);
    }

    goToLastPage() {
        if (!this.pagingData) return;
        this.loadPage(this.pagingData.numPages);
    }
}
