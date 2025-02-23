using System;
using tsx_aggregator.models;

using Constants = tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

public static class TestDataFactory {
    public const int DefaultInstrumentReportId = 100;
    public const int DefaultInstrumentId = 1;
    public const int DefaultReportType = (int)Constants.ReportTypes.CashFlow;
    public const int DefaultReportPeriodType = (int)Constants.ReportPeriodTypes.Annual;
    public static readonly DateTime Year2020Utc = new(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Year2020AsDate = DateOnly.FromDateTime(Year2020Utc);
    public static readonly DateTime Year2021Utc = new(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Year2021AsDate = DateOnly.FromDateTime(Year2021Utc);
    public const bool DefaultCheckManually = false;
    public const bool DefaultIgnoreReport = false;

    public static CurrentInstrumentRawDataReportDto CreateCurrentInstrumentReportDto(
        long? instrumentReportId = null,
        long? instrumentId = null,
        int? reportType = null,
        int? reportPeriodType = null,
        string reportJson = "{}",
        DateOnly? reportDate = null,
        bool? checkManually = null,
        bool? ignoreReport = null) {

        // Providing default values if null is passed for nullable parameters
        var instrumentReportId_ = instrumentReportId ?? DefaultInstrumentReportId;
        var instrumentId_ = instrumentId ?? DefaultInstrumentId;
        var reportType_ = reportType ?? DefaultReportType;
        var reportPeriodType_ = reportPeriodType ?? DefaultReportPeriodType;
        var reportDate_ = reportDate ?? Year2020AsDate;
        var checkManually_ = checkManually ?? DefaultCheckManually;
        var ignoreReport_ = ignoreReport ?? DefaultIgnoreReport;

        return new CurrentInstrumentRawDataReportDto(
            instrumentReportId_,
            instrumentId_,
            reportType_,
            reportPeriodType_,
            reportJson,
            reportDate_,
            checkManually_,
            ignoreReport_);
    }
}
