using System;
using System.Collections.Generic;

namespace tsx_aggregator.models;

public record DashboardStatsDto(
    long TotalActiveInstruments,
    long TotalObsoletedInstruments,
    long InstrumentsWithProcessedReports,
    DateTimeOffset? MostRecentRawIngestion,
    DateTimeOffset? MostRecentAggregation,
    long UnprocessedEventCount,
    IReadOnlyList<RawReportCountByTypeDto> RawReportCountsByType) {

    public long InstrumentsWithoutProcessedReports =>
        TotalActiveInstruments - InstrumentsWithProcessedReports;
}

public record RawReportCountByTypeDto(int ReportType, long Count);
