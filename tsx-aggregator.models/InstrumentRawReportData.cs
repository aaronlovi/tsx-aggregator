using System;

namespace tsx_aggregator.models;

public record InstrumentRawReportData(
    long InstrumentReportId,
    DateTime ReportCreatedDate,
    bool IsCurrent,
    bool CheckManually,
    bool IgnoreReport,
    string ReportJson);
