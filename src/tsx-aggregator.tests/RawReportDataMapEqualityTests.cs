using System;
using System.Text.Json;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class RawReportDataMapEqualityTests {
    [Fact]
    public void IsEqual_IdenticalNumericFields_ReturnsTrue() {
        var map = new RawReportDataMap { ["A"] = 1, ["B"] = 2 };
        using var json = JsonDocument.Parse("{\"A\": 1, \"B\": 2}");

        _ = map.IsEqual(json).Should().BeTrue();
    }

    [Fact]
    public void IsEqual_DifferentValue_ReturnsFalse() {
        var map = new RawReportDataMap { ["A"] = 1, ["B"] = 99 };
        using var json = JsonDocument.Parse("{\"A\": 1, \"B\": 2}");

        _ = map.IsEqual(json).Should().BeFalse();
    }

    [Fact]
    public void IsEqual_FieldOnlyInTheStoredReport_ReturnsTrue() {
        // Review P10: MergeWith keeps fields the new scrape lacks, so they cannot make the reports differ. Treating them
        // as a difference rewrote the same report, and raised a raw-data event, on every scrape (ENB: 14 of 30 reports,
        // a few hours after the previous scrape).
        var map = new RawReportDataMap { ["A"] = 1 };
        using var json = JsonDocument.Parse("{\"A\": 1, \"B\": 2}");

        _ = map.IsEqual(json).Should().BeTrue();
    }

    [Fact]
    public void IsEqual_ExtraFieldInMap_ReturnsFalse() {
        // Map has A and B, JSON only has A
        var map = new RawReportDataMap { ["A"] = 1, ["B"] = 2 };
        using var json = JsonDocument.Parse("{\"A\": 1}");

        _ = map.IsEqual(json).Should().BeFalse();
    }

    [Fact]
    public void IsEqual_NonNumericFieldsIgnored_ReturnsTrue() {
        // JSON has a string field (REPORTDATE) that should be ignored in equality comparison
        var map = new RawReportDataMap { ["A"] = 1 };
        using var json = JsonDocument.Parse("{\"A\": 1, \"REPORTDATE\": \"2020-01-01T00:00:00Z\"}");

        _ = map.IsEqual(json).Should().BeTrue();
    }

    [Fact]
    public void IsEqual_EmptyBothSides_ReturnsTrue() {
        var map = new RawReportDataMap();
        using var json = JsonDocument.Parse("{}");

        _ = map.IsEqual(json).Should().BeTrue();
    }

    [Fact]
    public void IsEqual_KeysMatchWhateverTheirCase() {
        // Map keys are upper-cased (NormalizedStringKeysHashMap); stored JSON keys are compared the same way (review R70).
        var map = new RawReportDataMap { ["DATA_POINT"] = 5 };
        using var json = JsonDocument.Parse("{\"data_point\": 5}");

        _ = map.IsEqual(json).Should().BeTrue();
    }

    [Fact]
    public void IsEqual_AfterMerging_TheSameScrapeAgainIsEqual() {
        // The delta tool merges an unequal report into the stored one; scraping the same data again must not
        // look like a change.
        var scrape = new RawReportDataMap { ReportDate = new DateOnly(2025, 12, 31), ["A"] = 1, ["B"] = 3 };
        using var stored = JsonDocument.Parse("{\"A\": 1, \"B\": 2, \"C\": 7, \"REPORTDATE\": \"2025-12-31T00:00:00Z\"}");
        _ = scrape.IsEqual(stored).Should().BeFalse("B changed");

        using var merged = JsonDocument.Parse(scrape.MergeWith(stored));

        _ = scrape.IsEqual(merged).Should().BeTrue();
    }
}
