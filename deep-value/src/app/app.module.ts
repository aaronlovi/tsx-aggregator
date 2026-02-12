import { APP_INITIALIZER, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { CompanyListComponent } from './company-list/company-list.component';

import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
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
import { CompactCurrencyPipe } from './pipes/compact-currency.pipe';
import { RelativeTimePipe } from './pipes/relative-time.pipe';
import { QuickSearchComponent } from './quick-search/quick-search.component';
import { AllCompaniesComponent } from './all-companies/all-companies.component';
import { UpdatedRawDataReportsComponent } from './updated-raw-data-reports/updated-raw-data-reports';
import { MissingDataComponent } from './missing-data/missing-data.component';
import { SystemControlsComponent } from './system-controls/system-controls.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { MatCardModule } from '@angular/material/card';



@NgModule({ declarations: [
        AppComponent,
        AllCompaniesComponent,
        CompanyListComponent,
        HeaderComponent,
        AboutComponent,
        CompanyDetailsComponent,
        QuickSearchComponent,
        UpdatedRawDataReportsComponent,
        MissingDataComponent,
        SystemControlsComponent,
        DashboardComponent,
        CompactCurrencyPipe,
        RelativeTimePipe
    ],
    bootstrap: [AppComponent], imports: [BrowserModule,
        AppRoutingModule,
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
        MatCardModule,
        FormsModule,
        CommonModule], providers: [
        {
            provide: APP_INITIALIZER,
            useFactory: appConfigInit,
            multi: true,
            deps: [AppConfigService]
        },
        provideHttpClient(withInterceptorsFromDi())
    ] })
export class AppModule {
}

export function appConfigInit(appConfigService: AppConfigService) {
    return () => {
        return appConfigService.load()
    };
}
