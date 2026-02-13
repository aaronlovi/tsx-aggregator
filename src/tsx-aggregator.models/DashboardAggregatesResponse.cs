using System.Collections.Generic;

namespace tsx_aggregator.models;

public record DashboardAggregatesResponse(
    int TotalCompanies,
    int CompaniesWithPriceData,
    int CompaniesWithoutPriceData,
    int CompaniesPassingAllChecks,
    decimal AverageEstimatedReturn_FromCashFlow,
    decimal AverageEstimatedReturn_FromOwnerEarnings,
    decimal MedianEstimatedReturn_FromCashFlow,
    decimal MedianEstimatedReturn_FromOwnerEarnings,
    decimal TotalMarketCap,
    IReadOnlyList<ScoreDistributionItem> ScoreDistribution,
    IReadOnlyList<ScoreCategoryStats> ScoreCategoryStatistics);

public record ScoreDistributionItem(int Score, int Count);

public record ScoreCategoryStats(
    int Score,
    int Count,
    decimal SumMarketCap,
    decimal MeanMarketCap,
    decimal MedianMarketCap,
    decimal MeanReturnFromCashFlow,
    decimal MedianReturnFromCashFlow,
    decimal MeanReturnFromOwnerEarnings,
    decimal MedianReturnFromOwnerEarnings);
