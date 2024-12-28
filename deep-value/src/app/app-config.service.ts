import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
    providedIn: 'root'
})
export class AppConfigService {
    public version: string;
    public apiEndpoint: string;

    constructor(private http: HttpClient) {
        this.version = '';
        this.apiEndpoint = '';
    }

    load(): Promise<any> {
        const promise = this.http.get('assets/app.config.json')
            .toPromise()
            .then(data => {
                Object.assign(this, data);
                return data;
            });

        return promise;
    }
}
