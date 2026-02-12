import { Component, OnInit } from '@angular/core';
import { CompanyService } from '../services/company.service';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-system-controls',
    templateUrl: './system-controls.component.html',
    styleUrls: ['./system-controls.component.scss'],
    standalone: false
})
export class SystemControlsComponent implements OnInit {
    pageTitle = 'System Controls';
    prioritySymbols: string[] = [];
    newSymbolsInput: string = '';
    loading: boolean = false;
    errorMsg: string = '';
    successMsg: string = '';

    constructor(
        public textService: TextService,
        private companyService: CompanyService
    ) { }

    ngOnInit() {
        this.loadPriorityQueue();
    }

    loadPriorityQueue() {
        this.loading = true;
        this.errorMsg = '';

        this.companyService.getPriorityCompanies().subscribe({
            next: (symbols: string[]) => {
                this.prioritySymbols = symbols;
                this.loading = false;
            },
            error: (error: any) => {
                this.errorMsg = 'Failed to load priority queue';
                this.loading = false;
            }
        });
    }

    setPriorityCompanies() {
        const symbols = this.newSymbolsInput
            .split(',')
            .map(s => s.trim().toUpperCase())
            .filter(s => s.length > 0);

        if (symbols.length === 0) {
            this.errorMsg = 'Please enter at least one company symbol';
            return;
        }

        this.loading = true;
        this.errorMsg = '';
        this.successMsg = '';

        this.companyService.setPriorityCompanies(symbols).subscribe({
            next: () => {
                this.successMsg = `Priority set for ${symbols.length} company symbol(s)`;
                this.newSymbolsInput = '';
                this.loadPriorityQueue();
            },
            error: (error: any) => {
                this.errorMsg = 'Failed to set priority companies';
                this.loading = false;
            }
        });
    }

    clearPriorityQueue() {
        this.loading = true;
        this.errorMsg = '';
        this.successMsg = '';

        this.companyService.setPriorityCompanies([]).subscribe({
            next: () => {
                this.successMsg = 'Priority queue cleared';
                this.loadPriorityQueue();
            },
            error: (error: any) => {
                this.errorMsg = 'Failed to clear priority queue';
                this.loading = false;
            }
        });
    }
}
