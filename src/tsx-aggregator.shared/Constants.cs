using System;

namespace tsx_aggregator.shared;

public static class Constants {
    public enum ReportTypes {
        Undefined = 0,
        CashFlow = 1,
        IncomeStatement = 2,
        BalanceSheet = 3
    }

    public enum ReportPeriodTypes {
        Undefined = 0,
        Annual = 1,
        Quarterly = 2,
        SemiAnnual = 3
    }

    public enum CompanyEventTypes {
        Undefined = 0,
        NewListedCompany = 1,
        UpdatedListedCompany = 2,
        ObsoletedListedCompany = 3,
        RawDataChanged = 4
    }

    public const string TsxExchange = "TSX";

    public static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);
}
