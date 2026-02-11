using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class DashboardStatsDtoTests {
    [Fact]
    public void InstrumentsWithoutProcessedReports_ShouldComputeCorrectly() {
        // Arrange
        var dto = new DashboardStatsDto(
            TotalActiveInstruments: 100,
            TotalObsoletedInstruments: 20,
            InstrumentsWithProcessedReports: 75,
            MostRecentRawIngestion: DateTimeOffset.UtcNow,
            MostRecentAggregation: DateTimeOffset.UtcNow,
            UnprocessedEventCount: 5,
            ManualReviewCount: 3,
            RawReportCountsByType: []);

        // Act & Assert
        _ = dto.InstrumentsWithoutProcessedReports.Should().Be(25);
    }

    [Fact]
    public void InstrumentsWithoutProcessedReports_WhenAllProcessed_ShouldBeZero() {
        // Arrange
        var dto = new DashboardStatsDto(
            TotalActiveInstruments: 50,
            TotalObsoletedInstruments: 10,
            InstrumentsWithProcessedReports: 50,
            MostRecentRawIngestion: null,
            MostRecentAggregation: null,
            UnprocessedEventCount: 0,
            ManualReviewCount: 0,
            RawReportCountsByType: []);

        // Act & Assert
        _ = dto.InstrumentsWithoutProcessedReports.Should().Be(0);
    }

    [Fact]
    public void RawReportCountsByType_ShouldPreserveValues() {
        // Arrange
        var counts = new List<RawReportCountByTypeDto> {
            new(ReportType: 1, Count: 500),
            new(ReportType: 2, Count: 300),
            new(ReportType: 3, Count: 400)
        };

        var dto = new DashboardStatsDto(
            TotalActiveInstruments: 100,
            TotalObsoletedInstruments: 0,
            InstrumentsWithProcessedReports: 80,
            MostRecentRawIngestion: null,
            MostRecentAggregation: null,
            UnprocessedEventCount: 0,
            ManualReviewCount: 0,
            RawReportCountsByType: counts);

        // Act & Assert
        _ = dto.RawReportCountsByType.Should().HaveCount(3);
        _ = dto.RawReportCountsByType[0].ReportType.Should().Be(1);
        _ = dto.RawReportCountsByType[0].Count.Should().Be(500);
        _ = dto.RawReportCountsByType[2].ReportType.Should().Be(3);
        _ = dto.RawReportCountsByType[2].Count.Should().Be(400);
    }

    [Fact]
    public void NullableDates_ShouldBeHandled() {
        // Arrange
        var dto = new DashboardStatsDto(
            TotalActiveInstruments: 0,
            TotalObsoletedInstruments: 0,
            InstrumentsWithProcessedReports: 0,
            MostRecentRawIngestion: null,
            MostRecentAggregation: null,
            UnprocessedEventCount: 0,
            ManualReviewCount: 0,
            RawReportCountsByType: []);

        // Act & Assert
        _ = dto.MostRecentRawIngestion.Should().BeNull();
        _ = dto.MostRecentAggregation.Should().BeNull();
    }
}
