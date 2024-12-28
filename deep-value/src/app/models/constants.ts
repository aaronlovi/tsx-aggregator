export enum ReportTypes {
    Undefined = 0,
    CashFlow = 1,
    IncomeStatement = 2,
    BalanceSheet = 3
}

export enum ReportPeriodTypes {
    Undefined = 0,
    Annual = 1,
    Quarterly = 2,
    SemiAnnual = 3
}

export enum CompanyEventTypes {
    Undefined = 0,
    NewListedCompany = 1,
    UpdatedListedCompany = 2,
    ObsoletedListedCompany = 3,
    RawDataChanged = 4
}

export function getReportTypeString(value: ReportTypes): string {
    switch (value) {
        case ReportTypes.Undefined:
            return "Undefined";
        case ReportTypes.CashFlow:
            return "Cash Flow";
        case ReportTypes.IncomeStatement:
            return "Income Statement";
        case ReportTypes.BalanceSheet:
            return "Balance Sheet";
        default:
            return "Unknown";
    }
}

export function getReportPeriodTypeString(value: ReportPeriodTypes): string {
    switch (value) {
        case ReportPeriodTypes.Undefined:
            return "Undefined";
        case ReportPeriodTypes.Annual:
            return "Annual";
        case ReportPeriodTypes.Quarterly:
            return "Quarterly";
        case ReportPeriodTypes.SemiAnnual:
            return "Semi-Annual";
        default:
            return "Unknown";
    }
}

export function getCompanyEventTypeString(value: CompanyEventTypes): string {
    switch (value) {
        case CompanyEventTypes.Undefined:
            return "Undefined";
        case CompanyEventTypes.NewListedCompany:
            return "New Listing";
        case CompanyEventTypes.UpdatedListedCompany:
            return "Updated Listing";
        case CompanyEventTypes.ObsoletedListedCompany:
            return "Obsoleted Listing";
        case CompanyEventTypes.RawDataChanged:
            return "Raw Data Changed";
        default:
            return "Unknown";
    }
}
