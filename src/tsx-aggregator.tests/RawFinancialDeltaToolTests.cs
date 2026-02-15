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
        SetupDI();
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        long instrumentId = 1;
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>(); // Populate with test data as needed
        var newRawCompanyData = new TsxCompanyData(); // Populate with test data as needed
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(instrumentId, existingRawFinancials, newRawCompanyData, cancellationToken);

        // Assert
        _ = result.Should().NotBeNull();
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_NoChanges_ReturnsNoDelta() {
        // Arrange
        SetupDI();
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        long instrumentReportId = TestDataFactory.DefaultInstrumentReportId;

        // Set up existing data
        var initialRawFinancial = CreateCurrentInstrumentReportDto(
            instrumentReportId: instrumentReportId,
            reportJson: "{\"DATA_POINT\": 1}");
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>() {
            initialRawFinancial,
            initialRawFinancial with {
                InstrumentReportId = ++instrumentReportId,
                ReportDate = TestDataFactory.Year2021AsDate,
                ReportJson = "{\"DATA_POINT\": 2}" }
        };

        // Set up incoming new data (same as the already existing data)
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["DATA_POINT"] = 1 });
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["DATA_POINT"] = 2 });

        var cancellationToken = CancellationToken.None;

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(
            TestDataFactory.DefaultInstrumentId,
            existingRawFinancials,
            newRawCompanyData,
            cancellationToken);

        // Assert
        _ = result.Should().NotBeNull();
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(0);
    }

    [Fact]
    public async Task TakeDelta_OneChangedProperty_ReturnsMergedUpdate() {
        // Arrange
        SetupDI();
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        long instrumentReportId = TestDataFactory.DefaultInstrumentReportId;

        // Set up existing data
        var initialRawFinancial = CreateCurrentInstrumentReportDto(
            instrumentReportId: instrumentReportId,
            reportJson: "{\"DATA_POINT\": 1}");
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>() {
            initialRawFinancial,
            initialRawFinancial with {
                InstrumentReportId = ++instrumentReportId,
                ReportDate = TestDataFactory.Year2021AsDate,
                ReportJson = "{\"DATA_POINT\": 2}" }
        };

        // Set up incoming new data - 2021 report has changed value
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["DATA_POINT"] = 1 });
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["DATA_POINT"] = 3 });

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(
            TestDataFactory.DefaultInstrumentId,
            existingRawFinancials,
            newRawCompanyData,
            CancellationToken.None);

        // Assert - merge produces an update, not insert+obsolete
        _ = result.Should().NotBeNull();
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);
        _ = result.InstrumentReportsToUpdate[0].MergedReportJson.Should().Contain("3");
    }

    [Fact]
    public async Task TakeDelta_NewFieldAdded_ReturnsMergedUpdate() {
        // Arrange
        SetupDI();
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        // Existing report has {A=1}
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>() {
            CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1}")
        };

        // New report has {A=1, B=2}
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1, ["B"] = 2 });

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(
            TestDataFactory.DefaultInstrumentId,
            existingRawFinancials,
            newRawCompanyData,
            CancellationToken.None);

        // Assert - new field added triggers merge update
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);

        // Verify merged JSON contains both A and B
        using var mergedDoc = System.Text.Json.JsonDocument.Parse(result.InstrumentReportsToUpdate[0].MergedReportJson);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("A").GetDecimal().Should().Be(1);
        _ = root.GetProperty("B").GetDecimal().Should().Be(2);
    }

    [Fact]
    public async Task TakeDelta_MissingFieldPreserved_ReturnsMergedUpdate() {
        // Arrange
        SetupDI();
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        // Existing report has {A=1, B=2}
        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto>() {
            CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1, \"B\": 2}")
        };

        // New report has only {A=1} - B is missing from new scrape
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1 });

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(
            TestDataFactory.DefaultInstrumentId,
            existingRawFinancials,
            newRawCompanyData,
            CancellationToken.None);

        // Assert - missing field B is preserved in merged result
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);

        // Verify merged JSON preserves B from existing
        using var mergedDoc = System.Text.Json.JsonDocument.Parse(result.InstrumentReportsToUpdate[0].MergedReportJson);
        var root = mergedDoc.RootElement;
        _ = root.GetProperty("A").GetDecimal().Should().Be(1);
        _ = root.GetProperty("B").GetDecimal().Should().Be(2);
    }

    private void SetupDI() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<RawFinancialDeltaTool>>(new NullLogger<RawFinancialDeltaTool>());
        _ = services.AddSingleton<ILogger<DbmInMemory>>(new NullLogger<DbmInMemory>());
        _ = services.AddSingleton<IDbmService, DbmInMemory>();
        _svp = services.BuildServiceProvider();
    }

    private static CurrentInstrumentRawDataReportDto CreateCurrentInstrumentReportDto(
        long? instrumentReportId = null,
        long? instrumentId = null,
        int? reportType = null,
        int? reportPeriodType = null,
        string reportJson = "{}",
        DateOnly? reportDate = null)
        => TestDataFactory.CreateCurrentInstrumentReportDto(instrumentReportId, instrumentId, reportType, reportPeriodType, reportJson, reportDate);
}
