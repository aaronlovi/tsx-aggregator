import { Component, OnInit, OnDestroy } from '@angular/core';
import { Subject, Subscription, debounceTime, distinctUntilChanged, switchMap, takeUntil } from 'rxjs';
import { CompanySearchService } from '../services/company-search.service';
import { CompanySearchItem } from '../models/company.search.item';
import { Router } from '@angular/router';

@Component({
    selector: 'app-quick-search',
    templateUrl: './quick-search.component.html',
    styleUrls: ['./quick-search.component.scss']
})
export class QuickSearchComponent implements OnInit, OnDestroy {
    searchTerm: string = '';
    searchResults: CompanySearchItem[] = [];
    private searchSubject: Subject<string> = new Subject<string>();
    private destroy$: Subject<void> = new Subject<void>();

    constructor(private searchService: CompanySearchService, private router: Router) { }

    ngOnInit(): void {
        this.setupSearchSubscription();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onSearchInputChange(): void {
        if (this.searchTerm.trim().length === 0) {
            this.clearSearchResults();
            return;
        }

        this.searchSubject.next(this.searchTerm);
    }

    onOptionSelected(exchange: string, instrumentSymbol: string): void {
        this.router.navigate(['company-details', exchange, instrumentSymbol]);
    }

    clearSearchResults(): void {
        this.searchResults = [];
    }

    private setupSearchSubscription(): void {
        this.searchSubject
            .pipe(
                debounceTime(250),
                distinctUntilChanged(),
                switchMap((searchTerm: string) => this.searchService.quickSearch(searchTerm).pipe(takeUntil(this.destroy$)))
            )
            .subscribe((results: CompanySearchItem[]) => {
                this.searchResults = results;
            });
    }

    displayFn(result: CompanySearchItem): string {
        console.log(`displayFn: ${result ? `ex: ${result.exchange} | sym: ${result.instrumentSymbol} | name: ${result.companyName}` : ''}`);
        return result ? `${result.exchange}:${result.instrumentSymbol}` : '';
    }
}
