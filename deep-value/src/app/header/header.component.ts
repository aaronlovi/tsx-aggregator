import { Component } from '@angular/core';
import { TranslateService } from '../services/translate.service';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-header',
    templateUrl: './header.component.html',
    styleUrls: ['./header.component.scss']
})
export class HeaderComponent {
    constructor(public textService: TextService, private translateService: TranslateService) { }

    setLocale(locale: string) {
        this.translateService.use(locale);
    }
}
