import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AboutComponent } from './about/about.component';
import { CompanyDetailsComponent } from './company-details/company-details.component';
import { CompanyListComponent } from './company-list/company-list.component';
import { UpdatedRawDataReportsComponent } from './updated-raw-data-reports/updated-raw-data-reports';

const routes: Routes = [
    { path: '', component: AboutComponent },
    { path: 'companies', component: CompanyListComponent, data: { mode: 'top' } },
    { path: 'companies/bottom', component: CompanyListComponent, data: { mode: 'bottom' } },
    { path: 'company-details/:exchange/:instrumentSymbol', component: CompanyDetailsComponent },
    { path: 'companies/updated_raw_data_reports', component: UpdatedRawDataReportsComponent },
    { path: '**', redirectTo: '/' }
];

@NgModule({
    imports: [RouterModule.forRoot(routes, {
    initialNavigation: 'enabledBlocking'
})],
    exports: [RouterModule]
})
export class AppRoutingModule { }
