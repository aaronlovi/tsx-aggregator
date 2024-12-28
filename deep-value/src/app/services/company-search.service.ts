import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { AppConfigService } from '../app-config.service';
import { CompanySearchItem } from '../models/company.search.item';

@Injectable({
    providedIn: 'root'
})
export class CompanySearchService {
    constructor(private http: HttpClient, private config: AppConfigService) { }

    quickSearch(searchTerm: string): Observable<CompanySearchItem[]> {
        return this.http.get<any[]>(`${this.config.apiEndpoint}/companies/quicksearch/${searchTerm}`).pipe(
            map(data => {
                return data.map(item => new CompanySearchItem(
                    item.exchange, item.instrumentSymbol, item.companyName
                ));
            })
        );
    }
}
