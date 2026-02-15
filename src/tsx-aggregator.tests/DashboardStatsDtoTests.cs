using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class DashboardStatsDtoTests {
    [Theory]
    [InlineData(100, 75, 25)]
    [InlineData(50, 50, 0)]
    [InlineData(0, 0, 0)]
    public void InstrumentsWithoutProcessedReports_ShouldComputeCorrectly(
        long totalActive, long withProcessed, long expected) {
        // Arrange
        var dto = TestDataFactory.CreateDashboardStatsDto(
            totalActiveInstruments: totalActive,
            instrumentsWithProcessedReports: withProcessed);

        // Act & Assert
        _ = dto.InstrumentsWithoutProcessedReports.Should().Be(expected);
    }

    [Fact]
    public void RawReportCountsByType_ShouldPreserveValues() {
        // Arrange
        var counts = new List<RawReportCountByTypeDto> {
            new(ReportType: 1, Count: 500),
            new(ReportType: 2, Count: 300),
            new(ReportType: 3, Count: 400)
        };

        var dto = TestDataFactory.CreateDashboardStatsDto(
            totalActiveInstruments: 100,
            instrumentsWithProcessedReports: 80,
            rawReportCountsByType: counts);

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
        var dto = TestDataFactory.CreateDashboardStatsDto();

        // Act & Assert
        _ = dto.MostRecentRawIngestion.Should().BeNull();
        _ = dto.MostRecentAggregation.Should().BeNull();
    }
}
