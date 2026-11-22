import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CompanyService } from '../services/company.service';

/** Shown when the web API refuses a change because it is read-only (production on the deploy laptop). */
export const READ_ONLY_MESSAGE =
    'This site is read-only, so it cannot change the priority queue. On the deploy laptop, use the tsx menu '
    + '(17 to 19) or tsx-ops priority.';

/** The message for a failed change: the read-only explanation for a 405, otherwise the given fallback. */
export function changeErrorMessage(error: unknown, fallback: string): string {
    const status = (error as { status?: number } | null)?.status;
    return status === 405 ? READ_ONLY_MESSAGE : fallback;
}

@Component({
    selector: 'app-system-controls',
    templateUrl: './system-controls.component.html',
    styleUrls: ['./system-controls.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
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
                this.errorMsg = changeErrorMessage(error, 'Failed to set priority companies');
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
                this.errorMsg = changeErrorMessage(error, 'Failed to clear priority queue');
                this.loading = false;
            }
        });
    }
}
