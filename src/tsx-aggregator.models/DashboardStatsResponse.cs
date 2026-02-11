using System;
using System.Collections.Generic;

namespace tsx_aggregator.models;

public record DashboardStatsResponse(
    long TotalActiveInstruments,
    long TotalObsoletedInstruments,
    long InstrumentsWithProcessedReports,
    long InstrumentsWithoutProcessedReports,
    DateTimeOffset? MostRecentRawIngestion,
    DateTimeOffset? MostRecentAggregation,
    long UnprocessedEventCount,
    long ManualReviewCount,
    IReadOnlyList<RawReportCountItem> RawReportCounts,
    DateTimeOffset? NextFetchDirectoryTime,
    DateTimeOffset? NextFetchInstrumentDataTime,
    DateTimeOffset? NextFetchQuotesTime,
    DateTimeOffset? NextAggregatorCycleTime);

public record RawReportCountItem(int ReportType, string ReportTypeName, long Count) {
    private static readonly Dictionary<int, string> ReportTypeNames = new() {
        { 1, "Cash Flow" },
        { 2, "Income Statement" },
        { 3, "Balance Sheet" }
    };

    public static RawReportCountItem FromTypeAndCount(int reportType, long count) {
        string name = ReportTypeNames.GetValueOrDefault(reportType, $"Unknown ({reportType})");
        return new RawReportCountItem(reportType, name, count);
    }
}
