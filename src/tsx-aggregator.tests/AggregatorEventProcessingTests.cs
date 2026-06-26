using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.Aggregated;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// Verifies that the Aggregator only marks an instrument event as processed
/// when handling it actually succeeded, so a transient failure during
/// aggregation does not silently drop the update.
/// </summary>
public class AggregatorEventProcessingTests {
    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);

    private Aggregator BuildAggregator() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<Aggregator>>(new NullLogger<Aggregator>());
        _ = services.AddSingleton<ILogger<AggregatorFsm>>(new NullLogger<AggregatorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        return new Aggregator(services.BuildServiceProvider());
    }

    private static InstrumentEventExDto Event(Constants.CompanyEventTypes type, long instrumentId = 1) =>
        new(
            new InstrumentEventDto(instrumentId, DateTimeOffset.UtcNow, (int)type, IsProcessed: false),
            InstrumentSymbol: "ABC",
            InstrumentName: "ABC Inc",
            Exchange: Constants.TsxExchange,
            PricePerShare: 1m,
            NumShares: 100);

    private void ExpectMarkProcessedReturns(Result result) =>
        _dbm.Setup(x => x.MarkInstrumentEventAsProcessed(It.IsAny<InstrumentEventExDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void VerifyMarkProcessed(Times times) =>
        _dbm.Verify(x => x.MarkInstrumentEventAsProcessed(It.IsAny<InstrumentEventExDto>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task RawDataChanged_WhenReadingCurrentReportsFails_DoesNotMarkProcessed() {
        // Arrange: a transient DB error while reading current reports.
        _dbm.Setup(x => x.GetCurrentInstrumentReports(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("db down"), (IReadOnlyList<CurrentInstrumentRawDataReportDto>)Array.Empty<CurrentInstrumentRawDataReportDto>()));

        // Act
        await BuildAggregator().ProcessInstrumentEvent(Event(Constants.CompanyEventTypes.RawDataChanged), CancellationToken.None);

        // Assert: the event must remain unprocessed so it can be retried.
        VerifyMarkProcessed(Times.Never());
    }

    [Fact]
    public async Task RawDataChanged_WhenNoCurrentReports_MarksProcessed() {
        // A data anomaly (no current raw reports) is not retryable; mark handled.
        _dbm.Setup(x => x.GetCurrentInstrumentReports(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (IReadOnlyList<CurrentInstrumentRawDataReportDto>)Array.Empty<CurrentInstrumentRawDataReportDto>()));
        ExpectMarkProcessedReturns(Result.SUCCESS);

        await BuildAggregator().ProcessInstrumentEvent(Event(Constants.CompanyEventTypes.RawDataChanged), CancellationToken.None);

        VerifyMarkProcessed(Times.Once());
    }

    [Theory]
    [InlineData(Constants.CompanyEventTypes.NewListedCompany)]
    [InlineData(Constants.CompanyEventTypes.UpdatedListedCompany)]
    [InlineData(Constants.CompanyEventTypes.ObsoletedListedCompany)]
    public async Task NonAggregatingEvents_AreMarkedProcessed(Constants.CompanyEventTypes type) {
        // These event types have no aggregation work, so they're always handled.
        ExpectMarkProcessedReturns(Result.SUCCESS);

        await BuildAggregator().ProcessInstrumentEvent(Event(type), CancellationToken.None);

        VerifyMarkProcessed(Times.Once());
    }
}
