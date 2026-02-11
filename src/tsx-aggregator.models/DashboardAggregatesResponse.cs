using System.Collections.Generic;

namespace tsx_aggregator.models;

public record DashboardAggregatesResponse(
    int TotalCompanies,
    int CompaniesWithPriceData,
    int CompaniesWithoutPriceData,
    int CompaniesPassingAllChecks,
    decimal AverageEstimatedReturn_FromCashFlow,
    decimal AverageEstimatedReturn_FromOwnerEarnings,
    IReadOnlyList<ScoreDistributionItem> ScoreDistribution);

public record ScoreDistributionItem(int Score, int Count);
