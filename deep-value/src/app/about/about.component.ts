import { Component } from '@angular/core';
import { TextService } from '../services/text.service';

@Component({
    selector: 'app-about',
    templateUrl: './about.component.html',
    styleUrls: ['./about.component.scss'],
    standalone: false
})
export class AboutComponent {

    constructor(public textService: TextService) { }
}
