using System;
using System.Text.Json;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class RawReportDataMapMergeTests {
    [Fact]
    public void MergeWith_OverlaysNewFields() {
        // Existing: {A=1, B=2, C=3}, New: {B=9, D=4} => Merged: {A=1, B=9, C=3, D=4}
        using var existingJson = JsonDocument.Parse("{\"A\": 1, \"B\": 2, \"C\": 3}");
        var newReport = new RawReportDataMap { ["B"] = 9, ["D"] = 4 };

        string merged = newReport.MergeWith(existingJson);

        TestDataFactory.AssertMergedJsonContains(merged, ("A", 1), ("B", 9), ("C", 3), ("D", 4));
    }

    [Fact]
    public void MergeWith_PreservesExistingReportDate() {
        using var existingJson = JsonDocument.Parse("{\"A\": 1, \"REPORTDATE\": \"2020-01-01T00:00:00Z\"}");
        var newReport = new RawReportDataMap { ["A"] = 1 };

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        _ = mergedDoc.RootElement.GetProperty("REPORTDATE").GetString().Should().Be("2020-01-01T00:00:00Z");
    }

    [Fact]
    public void MergeWith_NewReportDateOverwritesExisting() {
        using var existingJson = JsonDocument.Parse("{\"A\": 1, \"REPORTDATE\": \"2020-01-01T00:00:00Z\"}");
        var newReport = new RawReportDataMap {
            ReportDate = new DateOnly(2021, 6, 15),
            ["A"] = 1
        };

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        _ = mergedDoc.RootElement.GetProperty("REPORTDATE").GetString().Should().Be("2021-06-15T00:00:00Z");
    }

    [Fact]
    public void MergeWith_EmptyExisting() {
        using var existingJson = JsonDocument.Parse("{}");
        var newReport = new RawReportDataMap { ["A"] = 1 };

        string merged = newReport.MergeWith(existingJson);

        TestDataFactory.AssertMergedJsonContains(merged, ("A", 1));
    }

    [Fact]
    public void MergeWith_EmptyNew() {
        using var existingJson = JsonDocument.Parse("{\"A\": 1}");
        var newReport = new RawReportDataMap();

        string merged = newReport.MergeWith(existingJson);

        TestDataFactory.AssertMergedJsonContains(merged, ("A", 1));
    }

    [Fact]
    public void MergeWith_NormalizesExistingKeysToUppercase() {
        // Existing has lowercase key "data_point", new has uppercase "DATA_POINT"
        // MergeWith normalizes existing keys to uppercase, so the result should have one key
        using var existingJson = JsonDocument.Parse("{\"data_point\": 5}");
        var newReport = new RawReportDataMap { ["DATA_POINT"] = 10 };

        string merged = newReport.MergeWith(existingJson);

        TestDataFactory.AssertMergedJsonContains(merged, ("DATA_POINT", 10));
        // Should not have the lowercase version as a separate key
        using var mergedDoc = JsonDocument.Parse(merged);
        _ = mergedDoc.RootElement.TryGetProperty("data_point", out _).Should().BeFalse();
    }
}
