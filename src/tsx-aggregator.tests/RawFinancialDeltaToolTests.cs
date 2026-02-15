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
    public async Task TakeDelta_MissingFieldPreserved_ReturnsMergedUpdate() {
        // Arrange
        var tool = new RawFinancialDeltaTool(ServiceProvider);

        var existingRawFinancials = new List<CurrentInstrumentRawDataReportDto> {
            TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1, \"B\": 2}")
        };

        // New report has only {A=1} - B is missing from new scrape
        var newRawCompanyData = new TsxCompanyData();
        newRawCompanyData.AnnualRawCashFlowReports.Add(
            new RawReportDataMap { ReportDate = TestDataFactory.Year2020AsDate, ["A"] = 1 });

        // Act
        var result = await tool.TakeDelta(
            TestDataFactory.DefaultInstrumentId, existingRawFinancials, newRawCompanyData, CancellationToken.None);

        // Assert - missing field B is preserved in merged result
        _ = result.InstrumentReportsToInsert.Count.Should().Be(0);
        _ = result.InstrumentReportsToObsolete.Count.Should().Be(0);
        _ = result.InstrumentReportsToUpdate.Count.Should().Be(1);
        TestDataFactory.AssertMergedJsonContains(result.InstrumentReportsToUpdate[0].MergedReportJson, ("A", 1), ("B", 2));
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
