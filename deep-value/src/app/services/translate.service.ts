import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { LocalStorageService } from './local-storage.service';
import { Constants } from '../shared/constants';

@Injectable({
    providedIn: 'root'
})
export class TranslateService {
    data: any = {};

    constructor(private http: HttpClient, private localStorageService: LocalStorageService) { }

    use(newLocale: string): Promise<{}> {
        return new Promise<{}>(resolve => {
            const langPath = `assets/i18n/${newLocale || Constants.DefaultLocale}.json`;
            this.http.get(langPath).subscribe(
                response => {
                    this.data = response || {};
                    this.localStorageService.SiteLocale = newLocale;
                    resolve(this.data);
                },
                err => {
                    this.data = {};
                    resolve(this.data);
                }
            );
        });
    }
}
