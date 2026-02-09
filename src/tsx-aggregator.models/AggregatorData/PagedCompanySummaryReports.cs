using System.Collections.Generic;

namespace tsx_aggregator.models;

public record PagedCompanySummaryReports(
    PagingData PagingData,
    IEnumerable<CompanySummaryReport> Companies);
