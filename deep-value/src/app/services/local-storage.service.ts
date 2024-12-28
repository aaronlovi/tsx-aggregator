import { Injectable } from '@angular/core';
import { Constants } from '../shared/constants';

@Injectable({
    providedIn: 'root'
})
export class LocalStorageService {

    _siteLocale: string;

    constructor() { 
        this._siteLocale = localStorage.getItem(Constants.LocalStore__SiteLocaleKey) || Constants.DefaultLocale;
    }

    public get SiteLocale(): string { return this._siteLocale; }
    public set SiteLocale(val: string) {
        this._siteLocale = val;
        localStorage.setItem(Constants.LocalStore__SiteLocaleKey, val);
    }
}
