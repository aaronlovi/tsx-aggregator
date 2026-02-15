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

        using var mergedDoc = JsonDocument.Parse(merged);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("A").GetDecimal().Should().Be(1);
        _ = root.GetProperty("B").GetDecimal().Should().Be(9);
        _ = root.GetProperty("C").GetDecimal().Should().Be(3);
        _ = root.GetProperty("D").GetDecimal().Should().Be(4);
    }

    [Fact]
    public void MergeWith_PreservesExistingReportDate() {
        // Existing has REPORTDATE, new has no ReportDate => merged preserves existing REPORTDATE
        using var existingJson = JsonDocument.Parse("{\"A\": 1, \"REPORTDATE\": \"2020-01-01T00:00:00Z\"}");
        var newReport = new RawReportDataMap { ["A"] = 1 };

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("REPORTDATE").GetString().Should().Be("2020-01-01T00:00:00Z");
    }

    [Fact]
    public void MergeWith_NewReportDateOverwritesExisting() {
        // Existing has REPORTDATE, new has ReportDate set => merged uses new REPORTDATE
        using var existingJson = JsonDocument.Parse("{\"A\": 1, \"REPORTDATE\": \"2020-01-01T00:00:00Z\"}");
        var newReport = new RawReportDataMap {
            ReportDate = new DateOnly(2021, 6, 15),
            ["A"] = 1
        };

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("REPORTDATE").GetString().Should().Be("2021-06-15T00:00:00Z");
    }

    [Fact]
    public void MergeWith_EmptyExisting() {
        // Existing is {}, new has {A=1} => merged is {A=1}
        using var existingJson = JsonDocument.Parse("{}");
        var newReport = new RawReportDataMap { ["A"] = 1 };

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("A").GetDecimal().Should().Be(1);
    }

    [Fact]
    public void MergeWith_EmptyNew() {
        // Existing has {A=1}, new has no keys => merged is {A=1}
        using var existingJson = JsonDocument.Parse("{\"A\": 1}");
        var newReport = new RawReportDataMap();

        string merged = newReport.MergeWith(existingJson);

        using var mergedDoc = JsonDocument.Parse(merged);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("A").GetDecimal().Should().Be(1);
    }
}
