using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class DashboardAggregatesResponseTests {
    [Fact]
    public void DashboardAggregatesResponse_ShouldPreserveAllFields() {
        // Arrange
        var scoreDistribution = new List<ScoreDistributionItem> {
            new(Score: 15, Count: 5),
            new(Score: 14, Count: 10),
            new(Score: 13, Count: 15)
        };

        var scoreCategoryStatistics = new List<ScoreCategoryStats> {
            new(Score: 15, Count: 5, SumMarketCap: 500_000_000M, MeanMarketCap: 100_000_000M,
                MedianMarketCap: 90_000_000M, MeanReturnFromCashFlow: 12.50M, MedianReturnFromCashFlow: 11.00M,
                MeanReturnFromOwnerEarnings: 10.25M, MedianReturnFromOwnerEarnings: 9.50M),
            new(Score: 14, Count: 10, SumMarketCap: 800_000_000M, MeanMarketCap: 80_000_000M,
                MedianMarketCap: 75_000_000M, MeanReturnFromCashFlow: 8.75M, MedianReturnFromCashFlow: 7.50M,
                MeanReturnFromOwnerEarnings: 6.30M, MedianReturnFromOwnerEarnings: 5.80M)
        };

        // Act
        var response = new DashboardAggregatesResponse(
            TotalCompanies: 500,
            CompaniesWithPriceData: 450,
            CompaniesWithoutPriceData: 50,
            CompaniesPassingAllChecks: 5,
            AverageEstimatedReturn_FromCashFlow: 12.34M,
            AverageEstimatedReturn_FromOwnerEarnings: 8.76M,
            MedianEstimatedReturn_FromCashFlow: 10.50M,
            MedianEstimatedReturn_FromOwnerEarnings: 7.25M,
            TotalMarketCap: 1234567890.00M,
            ScoreDistribution: scoreDistribution,
            ScoreCategoryStatistics: scoreCategoryStatistics);

        // Assert
        _ = response.TotalCompanies.Should().Be(500);
        _ = response.CompaniesWithPriceData.Should().Be(450);
        _ = response.CompaniesWithoutPriceData.Should().Be(50);
        _ = response.CompaniesPassingAllChecks.Should().Be(5);
        _ = response.AverageEstimatedReturn_FromCashFlow.Should().Be(12.34M);
        _ = response.AverageEstimatedReturn_FromOwnerEarnings.Should().Be(8.76M);
        _ = response.MedianEstimatedReturn_FromCashFlow.Should().Be(10.50M);
        _ = response.MedianEstimatedReturn_FromOwnerEarnings.Should().Be(7.25M);
        _ = response.TotalMarketCap.Should().Be(1234567890.00M);
        _ = response.ScoreDistribution.Should().HaveCount(3);
        _ = response.ScoreDistribution[0].Score.Should().Be(15);
        _ = response.ScoreDistribution[0].Count.Should().Be(5);
        _ = response.ScoreCategoryStatistics.Should().HaveCount(2);
        _ = response.ScoreCategoryStatistics[0].Score.Should().Be(15);
        _ = response.ScoreCategoryStatistics[0].SumMarketCap.Should().Be(500_000_000M);
        _ = response.ScoreCategoryStatistics[1].Score.Should().Be(14);
        _ = response.ScoreCategoryStatistics[1].MeanReturnFromCashFlow.Should().Be(8.75M);
    }

    [Fact]
    public void ScoreDistributionItem_ShouldPreserveValues() {
        // Act
        var item = new ScoreDistributionItem(Score: 15, Count: 42);

        // Assert
        _ = item.Score.Should().Be(15);
        _ = item.Count.Should().Be(42);
    }
}
