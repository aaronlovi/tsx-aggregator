using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class RawFinancialDeltaToolTests {
    private ServiceProvider? _svp;

    private ServiceProvider ServiceProvider {
        get {
            if (_svp == null) {
                SetupDI();
            }
            return _svp!;
        }
    }

    [Fact]
    public async Task TakeDelta_NoReports_ReturnsExpectedDelta() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>();
        var newRawCompanyData = new TsxCompanyData();

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert
        _ = result.Should().NotBeNull();
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_NoChanges_ReturnsNoDelta() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        long instrumentReportId = TestDataFactory.DefaultInstrumentReportId;

        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(
                instrumentReportId: instrumentReportId,
                reportJson: "{\"DATA_POINT\": 1}"),
            TestDataFactory.CreateCurrentInstrumentReportDto(
                instrumentReportId: instrumentReportId + 1,
                reportDate: TestDataFactory.Year2021AsDate,
                reportJson: "{\"DATA_POINT\": 2}")
        };

        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["DATA_POINT"] = 1 });
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["DATA_POINT"] = 2 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert
        _ = result.Should().NotBeNull();
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_OneChangedProperty_ReturnsMergedUpdate() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        long instrumentReportId = TestDataFactory.DefaultInstrumentReportId;

        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(
                instrumentReportId: instrumentReportId,
                reportJson: "{\"DATA_POINT\": 1}"),
            TestDataFactory.CreateCurrentInstrumentReportDto(
                instrumentReportId: instrumentReportId + 1,
                reportDate: TestDataFactory.Year2021AsDate,
                reportJson: "{\"DATA_POINT\": 2}")
        };

        // 2021 report has changed value
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["DATA_POINT"] = 1 });
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["DATA_POINT"] = 3 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);
        TestDataFactory.AssertMergedJsonContains(result.InstrumentReportsToUpdate[0].MergedReportJson, ("DATA_POINT", 3));
    }

    [Fact]
    public async Task TakeDelta_NewFieldAdded_ReturnsMergedUpdate() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);

        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1}")
        };

        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1, ["B"] = 2 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);
        TestDataFactory.AssertMergedJsonContains(result.InstrumentReportsToUpdate[0].MergedReportJson, ("A", 1), ("B", 2));
    }

    [Fact]
    public async Task TakeDelta_FieldMissingFromTheNewScrape_LeavesTheStoredReportAlone() {
        // Review P10: merging would write back exactly what is stored, so there is nothing to update. This used to
        // produce an update, and a raw-data event, on every scrape.
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1, \"B\": 2}")
        };
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1 });

        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_ChangedValueWithAFieldMissing_KeepsTheMissingFieldInTheMerge() {
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1, \"B\": 2}")
        };
        // New report has A changed to 5, and B is missing from the new scrape
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 5 });

        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);
        TestDataFactory.AssertMergedJsonContains(result.InstrumentReportsToUpdate[0].MergedReportJson, ("A", 5), ("B", 2));
    }

    [Fact]
    public async Task TakeDelta_NewReportDate_InsertsNewReport() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);

        // Existing: only 2020 report
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1}")
        };

        // New: 2020 (unchanged) + 2021 (brand new)
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1 });
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["B"] = 2 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert
        _ = result.InstrumentReportsToInsert.Count.Should().Be(1);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
    }

    // Review R31: reports were matched to stored ones by calendar year (annual) or calendar quarter (quarterly), so a
    // second report in the same year or quarter (a changed fiscal year end) was merged into the first one's row, under
    // its date. Found in the dev data: 18 annual rows of 10 companies (DOO's 2022-01-31 row held its 2022-12-31 report)
    // and 2 quarterly rows (RUP 2024-11-30 / 2024-12-31). No stored dates drift by 10 days or less, so the match is exact.

    private static readonly DateOnly Jan2022 = new(2022, 1, 31);
    private static readonly DateOnly Dec2022 = new(2022, 12, 31);
    private static readonly DateOnly Nov2024 = new(2024, 11, 30);
    private static readonly DateOnly Dec2024 = new(2024, 12, 31);

    private async Task<RawFinancialsDelta> DeltaOf(CurrentInstrumentRawDataReportDto existing, Action<TsxCompanyData> addNew) {
        var newRawCompanyData = new TsxCompanyData();
        addNew(newRawCompanyData);
        return await new RawFinancialDeltaTool(ServiceProvider).TakeDelta(
            TestDataFactory.DefaultInstrumentId, [existing], newRawCompanyData, CancellationToken.None);
    }

    [Fact]
    public async Task TakeDelta_SecondAnnualReportInTheSameYear_IsInsertedNotMerged() {
        RawFinancialsDelta result = await DeltaOf(
            TestDataFactory.CreateCurrentInstrumentReportDto(reportDate: Jan2022, reportJson: "{\"A\": 1}"),
            d => d.AnnualRawCashFlowReports.Add(new RawReportDataMap { ReportDate = Dec2022, ["A"] = 5 }));

        _ = result.InstrumentReportsToUpdate.Should().BeEmpty("the January row must keep its own figures");
        _ = result.InstrumentReportsToInsert.Should().ContainSingle().Which.ReportDate.Should().Be(Dec2022);
    }

    [Fact]
    public async Task TakeDelta_SecondQuarterlyReportInTheSameQuarter_IsInsertedNotMerged() {
        RawFinancialsDelta result = await DeltaOf(
            TestDataFactory.CreateCurrentInstrumentReportDto(
                reportPeriodType: (int)tsx_aggregator.shared.Constants.ReportPeriodTypes.Quarterly, reportDate: Nov2024, reportJson: "{\"A\": 1}"),
            d => d.QuarterlyRawCashFlowReports.Add(new RawReportDataMap { ReportDate = Dec2024, ["A"] = 5 }));

        _ = result.InstrumentReportsToUpdate.Should().BeEmpty();
        _ = result.InstrumentReportsToInsert.Should().ContainSingle().Which.ReportDate.Should().Be(Dec2024);
    }

    [Fact]
    public async Task TakeDelta_ChangedQuarterlyReport_SameDate_IsMerged() {
        RawFinancialsDelta result = await DeltaOf(
            TestDataFactory.CreateCurrentInstrumentReportDto(
                reportPeriodType: (int)tsx_aggregator.shared.Constants.ReportPeriodTypes.Quarterly, reportDate: Dec2024, reportJson: "{\"A\": 1}"),
            d => d.QuarterlyRawCashFlowReports.Add(new RawReportDataMap { ReportDate = Dec2024, ["A"] = 5 }));

        _ = result.InstrumentReportsToInsert.Should().BeEmpty();
        _ = result.InstrumentReportsToUpdate.Should().ContainSingle();
        TestDataFactory.AssertMergedJsonContains(result.InstrumentReportsToUpdate[0].MergedReportJson, ("A", 5));
    }

    [Fact]
    public async Task TakeDelta_InvalidReport_IsSkipped() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>();

        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, IsValid = false, ["A"] = 1 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert - invalid report should be skipped entirely
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_NullReportDate_IsSkipped() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>();

        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ["A"] = 1 }); // ReportDate defaults to null

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert - report with null date should be skipped
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
    }

    private void SetupDI() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<RawFinancialDeltaTool>>(new NullLogger<RawFinancialDeltaTool>());
        _ = services.AddSingleton<ILogger<DbmInMemory>>(new NullLogger<DbmInMemory>());
        _ = services.AddSingleton<IDbmService, DbmInMemory>();
        _svp = services.BuildServiceProvider();
    }
}
