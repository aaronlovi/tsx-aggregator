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
    public void IsEqual_ExtraFieldInJson_ReturnsFalse() {
        // JSON has A and B, map only has A
        var map = new RawReportDataMap { ["A"] = 1 };
        using var json = JsonDocument.Parse("{\"A\": 1, \"B\": 2}");

        _ = map.IsEqual(json).Should().BeFalse();
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
    public void IsEqual_CaseSensitiveKeyLookup() {
        // IsEqual uses TryGetProperty which is case-sensitive on JsonDocument.
        // Map keys are stored uppercase via NormalizedStringKeysHashMap.
        // If JSON has lowercase keys, IsEqual will not find them via TryGetProperty.
        var map = new RawReportDataMap { ["DATA_POINT"] = 5 };
        using var json = JsonDocument.Parse("{\"data_point\": 5}");

        // The map key "DATA_POINT" won't match JSON property "data_point" via TryGetProperty
        _ = map.IsEqual(json).Should().BeFalse();
    }
}
