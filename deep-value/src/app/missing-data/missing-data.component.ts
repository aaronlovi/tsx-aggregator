import { Component, OnInit } from '@angular/core';
import { PagingData } from '../models/paging_data';
import { CompanyService } from '../services/company.service';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-missing-data',
    templateUrl: './missing-data.component.html',
    styleUrls: ['./missing-data.component.scss'],
    standalone: false
})
export class MissingDataComponent implements OnInit {
    displayedColumns: string[] = ['instrumentSymbol', 'companyName', 'instrumentName'];
    instruments: any[] = [];
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
        this.instruments = [];
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getMissingDataCompanies('TSX', pageNumber, this.pageSize).subscribe({
            next: (data: any) => {
                this.pagingData = new PagingData(
                    data.pagingData.totalItems,
                    data.pagingData.pageNumber,
                    data.pagingData.pageSize
                );
                this.instruments = data.instruments;
                this.loading = false;
            },
            error: (error: any) => {
                this.errorMsg = 'An error occurred while fetching missing data companies';
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
