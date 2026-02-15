import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AboutComponent } from './about/about.component';
import { CompanyDetailsComponent } from './company-details/company-details.component';
import { CompanyListComponent } from './company-list/company-list.component';
import { AllCompaniesComponent } from './all-companies/all-companies.component';
import { MissingDataComponent } from './missing-data/missing-data.component';
import { SystemControlsComponent } from './system-controls/system-controls.component';
import { DashboardComponent } from './dashboard/dashboard.component';

const routes: Routes = [
    { path: '', component: AboutComponent },
    { path: 'dashboard', component: DashboardComponent },
    { path: 'companies', component: CompanyListComponent, data: { mode: 'top' } },
    { path: 'companies/bottom', component: CompanyListComponent, data: { mode: 'bottom' } },
    { path: 'companies/all', component: AllCompaniesComponent },
    { path: 'companies/missing_data', component: MissingDataComponent },
    { path: 'company-details/:exchange/:instrumentSymbol', component: CompanyDetailsComponent },
    { path: 'system-controls', component: SystemControlsComponent },
    { path: '**', redirectTo: '/' }
];

@NgModule({
    imports: [RouterModule.forRoot(routes, {
    initialNavigation: 'enabledBlocking'
})],
    exports: [RouterModule]
})
export class AppRoutingModule { }
