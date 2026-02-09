import { APP_INITIALIZER, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { CompanyListComponent } from './company-list/company-list.component';

import { HttpClientModule } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AppConfigService } from './app-config.service';

import { CommonModule } from '@angular/common'; // Import CommonModule
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { AboutComponent } from './about/about.component';
import { CompanyDetailsComponent } from './company-details/company-details.component';
import { HeaderComponent } from './header/header.component';
import { TranslatePipe } from './pipes/translate.pipe';
import { QuickSearchComponent } from './quick-search/quick-search.component';
import { LocalStorageService } from './services/local-storage.service';
import { TranslateService } from './services/translate.service';
import { TranslocoRootModule } from './transloco-root.module';
import { UpdatedRawDataReportsComponent } from './updated-raw-data-reports/updated-raw-data-reports';



@NgModule({
    declarations: [
        AppComponent,
        CompanyListComponent,
        HeaderComponent,
        AboutComponent,
        CompanyDetailsComponent,
        QuickSearchComponent,
        UpdatedRawDataReportsComponent,
        TranslatePipe
    ],
    imports: [
        BrowserModule,
        AppRoutingModule,
        HttpClientModule,
        NoopAnimationsModule,
        BrowserModule,
        BrowserAnimationsModule,
        MatIconModule,
        MatButtonModule,
        MatTableModule,
        MatExpansionModule,
        MatInputModule,
        MatFormFieldModule,
        MatAutocompleteModule,
        MatListModule,
        MatSidenavModule,
        MatToolbarModule,
        MatTooltipModule,
        FormsModule,
        TranslocoRootModule,
        CommonModule],
    providers: [
        LocalStorageService,
        TranslateService,
        {
            provide: APP_INITIALIZER,
            useFactory: setupTranslateServiceFactory,
            multi: true,
            deps: [TranslateService, LocalStorageService]
        },
        {
            provide: APP_INITIALIZER,
            useFactory: appConfigInit,
            multi: true,
            deps: [AppConfigService]
        }
    ],
    bootstrap: [AppComponent]
})
export class AppModule {
}

export function setupTranslateServiceFactory(translateService: TranslateService, localStorageService: LocalStorageService): Function {
    return () => translateService.use(`${localStorageService.SiteLocale}`);
}

export function appConfigInit(appConfigService: AppConfigService) {
    return () => {
        return appConfigService.load()
    };
}
