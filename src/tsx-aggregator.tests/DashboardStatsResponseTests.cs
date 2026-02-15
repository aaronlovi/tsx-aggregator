using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class DashboardStatsResponseTests {
    [Fact]
    public void RawReportCountItem_FromTypeAndCount_KnownType_ShouldMapName() {
        // Act
        var item = RawReportCountItem.FromTypeAndCount(1, 500);

        // Assert
        _ = item.ReportType.Should().Be(1);
        _ = item.ReportTypeName.Should().Be("Cash Flow");
        _ = item.Count.Should().Be(500);
    }

    [Fact]
    public void RawReportCountItem_FromTypeAndCount_AllKnownTypes_ShouldMapCorrectly() {
        // Act & Assert
        _ = RawReportCountItem.FromTypeAndCount(1, 10).ReportTypeName.Should().Be("Cash Flow");
        _ = RawReportCountItem.FromTypeAndCount(2, 20).ReportTypeName.Should().Be("Income Statement");
        _ = RawReportCountItem.FromTypeAndCount(3, 30).ReportTypeName.Should().Be("Balance Sheet");
    }

    [Fact]
    public void RawReportCountItem_FromTypeAndCount_UnknownType_ShouldShowUnknown() {
        // Act
        var item = RawReportCountItem.FromTypeAndCount(99, 42);

        // Assert
        _ = item.ReportTypeName.Should().Be("Unknown (99)");
    }

    [Fact]
    public void DashboardStatsResponse_ShouldPreserveAllFields() {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var counts = new List<RawReportCountItem> {
            RawReportCountItem.FromTypeAndCount(1, 100),
            RawReportCountItem.FromTypeAndCount(2, 200)
        };

        var nextDir = now.AddHours(1);
        var nextInst = now.AddHours(2);
        var nextQuotes = now.AddHours(3);
        var nextAggregator = now.AddMinutes(2);

        // Act
        var response = new DashboardStatsResponse(
            TotalActiveInstruments: 1000,
            TotalObsoletedInstruments: 50,
            InstrumentsWithProcessedReports: 800,
            InstrumentsWithoutProcessedReports: 200,
            MostRecentRawIngestion: now,
            MostRecentAggregation: now,
            UnprocessedEventCount: 15,
            RawReportCounts: counts,
            NextFetchDirectoryTime: nextDir,
            NextFetchInstrumentDataTime: nextInst,
            NextFetchQuotesTime: nextQuotes,
            NextAggregatorCycleTime: nextAggregator);

        // Assert
        _ = response.TotalActiveInstruments.Should().Be(1000);
        _ = response.TotalObsoletedInstruments.Should().Be(50);
        _ = response.InstrumentsWithProcessedReports.Should().Be(800);
        _ = response.InstrumentsWithoutProcessedReports.Should().Be(200);
        _ = response.MostRecentRawIngestion.Should().Be(now);
        _ = response.MostRecentAggregation.Should().Be(now);
        _ = response.UnprocessedEventCount.Should().Be(15);
        _ = response.RawReportCounts.Should().HaveCount(2);
        _ = response.NextFetchDirectoryTime.Should().Be(nextDir);
        _ = response.NextFetchInstrumentDataTime.Should().Be(nextInst);
        _ = response.NextFetchQuotesTime.Should().Be(nextQuotes);
        _ = response.NextAggregatorCycleTime.Should().Be(nextAggregator);
    }
}
