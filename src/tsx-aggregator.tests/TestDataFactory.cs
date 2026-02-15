using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.Raw;

using Constants = tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

public static class TestDataFactory {
    public const int DefaultInstrumentReportId = 100;
    public const int DefaultInstrumentId = 1;
    public const int DefaultReportType = (int)Constants.ReportTypes.CashFlow;
    public const int DefaultReportPeriodType = (int)Constants.ReportPeriodTypes.Annual;
    public static readonly DateTime Year2020Utc = new(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Year2020AsDate = DateOnly.FromDateTime(Year2020Utc);
    public static readonly DateTime Year2021Utc = new(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Year2021AsDate = DateOnly.FromDateTime(Year2021Utc);

    public static CurrentInstrumentRawDataReportDto CreateCurrentInstrumentReportDto(
        long? instrumentReportId = null,
        long? instrumentId = null,
        int? reportType = null,
        int? reportPeriodType = null,
        string reportJson = "{}",
        DateOnly? reportDate = null) {

        return new CurrentInstrumentRawDataReportDto(
            instrumentReportId ?? DefaultInstrumentReportId,
            instrumentId ?? DefaultInstrumentId,
            reportType ?? DefaultReportType,
            reportPeriodType ?? DefaultReportPeriodType,
            reportJson,
            reportDate ?? Year2020AsDate);
    }

    public static DashboardStatsDto CreateDashboardStatsDto(
        long totalActiveInstruments = 0,
        long totalObsoletedInstruments = 0,
        long instrumentsWithProcessedReports = 0,
        DateTimeOffset? mostRecentRawIngestion = null,
        DateTimeOffset? mostRecentAggregation = null,
        long unprocessedEventCount = 0,
        IReadOnlyList<RawReportCountByTypeDto>? rawReportCountsByType = null) {

        return new DashboardStatsDto(
            totalActiveInstruments,
            totalObsoletedInstruments,
            instrumentsWithProcessedReports,
            mostRecentRawIngestion,
            mostRecentAggregation,
            unprocessedEventCount,
            rawReportCountsByType ?? []);
    }

    internal static Registry CreateRegistryWithInstruments(
        params (string CompanySymbol, string InstrumentSymbol, string Exchange)[] instruments) {
        var registry = new Registry();
        var list = new List<InstrumentDto>();
        long id = 1;
        foreach (var (cs, ins, ex) in instruments) {
            list.Add(new InstrumentDto(id++, ex, cs, cs + " Inc.", ins, ins + " Common", DateTimeOffset.UtcNow, null));
        }
        registry.InitializeDirectory(list);
        return registry;
    }

    public static void AssertMergedJsonContains(
        string mergedJson, params (string Key, decimal Value)[] expectedFields) {
        using var doc = JsonDocument.Parse(mergedJson);
        var root = doc.RootElement;
        foreach (var (key, value) in expectedFields) {
            _ = root.GetProperty(key).GetDecimal().Should().Be(value,
                $"because merged JSON should contain {key}={value}");
        }
    }
}
