using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.shared;

public static class Extensions {
    public static ReportTypes ToReportType(this int reportType) {
        return reportType switch {
            1 => Constants.ReportTypes.CashFlow,
            2 => Constants.ReportTypes.IncomeStatement,
            3 => Constants.ReportTypes.BalanceSheet,
            _ => Constants.ReportTypes.Undefined
        };
    }

    public static ReportPeriodTypes ToReportPeriodType(this int reportPeriodType) {
        return reportPeriodType switch {
            1 => Constants.ReportPeriodTypes.Annual,
            2 => Constants.ReportPeriodTypes.Quarterly,
            3 => Constants.ReportPeriodTypes.SemiAnnual,
            _ => Constants.ReportPeriodTypes.Undefined
        };
    }

    public static CompanyEventTypes ToCompanyEventType(this int companyEventType) {
        return companyEventType switch {
            1 => Constants.CompanyEventTypes.NewListedCompany,
            2 => Constants.CompanyEventTypes.UpdatedListedCompany,
            3 => Constants.CompanyEventTypes.ObsoletedListedCompany,
            4 => Constants.CompanyEventTypes.RawDataChanged,
            _ => Constants.CompanyEventTypes.Undefined
        };
    }

    public static bool IsValid(this ReportPeriodTypes rpt) {
        return rpt switch {
            ReportPeriodTypes.Annual => true,
            ReportPeriodTypes.Quarterly => true,
            ReportPeriodTypes.SemiAnnual => true,

            ReportPeriodTypes.Undefined => false,
            _ => false,
        };
    }

    public static bool IsValid(this ReportTypes rt) {
        return rt switch {
            ReportTypes.CashFlow => true,
            ReportTypes.IncomeStatement => true,
            ReportTypes.BalanceSheet => true,

            ReportTypes.Undefined => false,
            _ => false,
        };
    }

    public static DateTime EnsureUtc(this DateTime dt) {
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    public static DateTime ToDateTimeUtc(this DateOnly dateOnly) {
        return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    public static bool StartsWithOrdinal(this string str, string value) {
        ArgumentNullException.ThrowIfNull(str);
        return str.StartsWith(value, StringComparison.Ordinal);
    }

    public static bool EndsWithOrdinal(this string str, string value) {
        ArgumentNullException.ThrowIfNull(str);
        return str.EndsWith(value, StringComparison.Ordinal);
    }

    public static bool ContainsOrdinal(this string str, string value) {
        ArgumentNullException.ThrowIfNull(str);
        return str.Contains(value, StringComparison.Ordinal);
    }

    public static bool EqualsOrdinal(this string str, string value) {
        ArgumentNullException.ThrowIfNull(str);
        return str.Equals(value, StringComparison.Ordinal);
    }

    public static Timestamp ToTimestamp(this DateOnly dateOnly) {
        return Timestamp.FromDateTime(dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    public static DateOnly ToDateOnly(this Timestamp ts) {
        ArgumentNullException.ThrowIfNull(ts);
        return DateOnly.FromDateTime(ts.ToDateTime());
    }
}
