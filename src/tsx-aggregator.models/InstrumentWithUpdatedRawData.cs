using System;
using System.Collections.Generic;

namespace tsx_aggregator.models;

public record InstrumentWithConflictingRawData(
    long InstrumentId,
    string Exchange,
    string CompanySymbol,
    string InstrumentSymbol,
    string CompanyName,
    string InstrumentName,
    int ReportType,
    int ReportPeriodType,
    DateOnly ReportDate,
    IEnumerable<InstrumentRawReportData> ConflictingRawReports
);
