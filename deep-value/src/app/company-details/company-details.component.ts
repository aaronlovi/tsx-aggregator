import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CompanyService } from '../services/company.service';
import { CompanyDetails } from '../models/company.details';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-company-details',
    templateUrl: './company-details.component.html',
    styleUrls: ['./company-details.component.scss']
})
export class CompanyDetailsComponent implements OnInit {
    exchange: string;
    instrumentSymbol: string;
    companyDetails?: CompanyDetails;
    loading: boolean;
    errorMsg: string;
    
    constructor(public textService: TextService, private companyService: CompanyService, private route: ActivatedRoute) {
        this.exchange = '';
        this.instrumentSymbol = '';
        this.companyDetails = undefined;
        this.loading = false;
        this.errorMsg = '';
    }

    ngOnInit(): void {
        this.route.paramMap.subscribe(params => {
            this.exchange = params.get('exchange') || '';
            this.instrumentSymbol = params.get('instrumentSymbol') || '';

            console.log(`Exchange = ${this.exchange}, Instrument Symbol: ${this.instrumentSymbol}`);

            if (this.isInputsValid()) {
                this.getCompanyDetails();
            } else {
                this.errorMsg = 'Invalid exchange or instrument symbol';
            }
        });
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
                this.errorMsg = 'An error occurred while fetching companies data';
                this.loading = false;
            }
        );
    }
}
