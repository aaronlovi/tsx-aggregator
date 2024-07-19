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
        var existingRawFinancials = new List<CurrentInstrumentReportDto>(); // Populate with test data as needed
        var newRawCompanyData = new TsxCompanyData(); // Populate with test data as needed
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(instrumentId, existingRawFinancials, newRawCompanyData, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.InstrumentReportsToInsert.Count.Should().Be(0);
        result.InstrumentReportsToObsolete.Count.Should().Be(0);
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
        var existingRawFinancials = new List<CurrentInstrumentReportDto>() {
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
        result.Should().NotBeNull();
        result.InstrumentReportsToInsert.Count.Should().Be(0);
        result.InstrumentReportsToObsolete.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(true, 1, 0)] // Feature flag on: Insert new report to check manually. No reports to obsolete.
    [InlineData(false, 1, 1)] // Feature flag off: Insert new report and obsolete the existing report.
    public async Task TakeDelta_OneChangedProperty_ReturnsExpectedDelta(
        bool checkExistingRawReportUpdates,
        int numExpectedReportsToInsert,
        int numExpectedReportsToDelete) {
        // Arrange
        SetupDI(checkExistingRawReportUpdates);
        var rawFinancialDeltaTool = new RawFinancialDeltaTool(ServiceProvider);

        long instrumentReportId = TestDataFactory.DefaultInstrumentReportId;

        // Set up existing data
        var initialRawFinancial = CreateCurrentInstrumentReportDto(
            instrumentReportId: instrumentReportId,
            reportJson: "{\"DATA_POINT\": 1}");
        var existingRawFinancials = new List<CurrentInstrumentReportDto>() {
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
            new RawReportDataMap { ReportDate = TestDataFactory.Year2021AsDate, ["DATA_POINT"] = 3 });

        // Note: In existing 2021 Cash flow, data point is '2'. In new 2021 Cash flow, data point is '3'.

        var cancellationToken = CancellationToken.None;

        // Act
        var result = await rawFinancialDeltaTool.TakeDelta(
            TestDataFactory.DefaultInstrumentId,
            existingRawFinancials,
            newRawCompanyData,
            cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.InstrumentReportsToInsert.Count.Should().Be(numExpectedReportsToInsert);
        result.InstrumentReportsToObsolete.Count.Should().Be(numExpectedReportsToDelete);
        result.InstrumentReportsToInsert.Should().OnlyContain(
            report => report.CheckManually == checkExistingRawReportUpdates,
            checkExistingRawReportUpdates
                ? "because delta tool mode is: inserting new reports to check manually"
                : "because delta tool mode is: inserting new report and obsoleting existing report");
    }

    private void SetupDI(bool checkExistingRawReportUpdates = true) {
        var services = new ServiceCollection();
        var featureFlagsOptions = new FeatureFlagsOptions { CheckExistingRawReportUpdates = checkExistingRawReportUpdates };
        services.AddSingleton(featureFlagsOptions);
        services.AddSingleton<ILogger<RawFinancialDeltaTool>>(new NullLogger<RawFinancialDeltaTool>());
        services.AddSingleton<ILogger<DbmInMemory>>(new NullLogger<DbmInMemory>());
        services.AddSingleton<IDbmService, DbmInMemory>(); // Assuming DbmInMemory implements IDbmService
        _svp = services.BuildServiceProvider();
    }

    private static CurrentInstrumentReportDto CreateCurrentInstrumentReportDto(
        long? instrumentReportId = null,
        long? instrumentId = null,
        int? reportType = null,
        int? reportPeriodType = null,
        string reportJson = "{}",
        DateOnly? reportDate = null,
        bool? checkManually = null)
        => TestDataFactory.CreateCurrentInstrumentReportDto(instrumentReportId, instrumentId, reportType, reportPeriodType, reportJson, reportDate, checkManually);
}
