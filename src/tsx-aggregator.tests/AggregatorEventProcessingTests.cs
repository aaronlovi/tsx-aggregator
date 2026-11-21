using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
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

    #region Catch-up: a backlog is processed without waiting between events

    /// <summary>Runs the aggregator's main loop until <paramref name="done"/> or the deadline, then stops it.</summary>
    private async Task RunUntil(Func<bool> done, TimeSpan deadline) {
        _dbm.Setup(x => x.GetCommonServiceState(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, false));
        Aggregator aggregator = BuildAggregator();
        await aggregator.StartAsync(CancellationToken.None);
        DateTime stopAt = DateTime.UtcNow + deadline;
        while (!done() && DateTime.UtcNow < stopAt)
            await Task.Delay(20);
        await aggregator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Backlog_IsProcessedWithoutWaitingBetweenEvents() {
        // Three queued events, then an empty queue. Before the fix the loop slept 5 s before the first check and
        // between events (15 s for three).
        var queue = new Queue<InstrumentEventExDto>([
            Event(Constants.CompanyEventTypes.NewListedCompany, 1),
            Event(Constants.CompanyEventTypes.NewListedCompany, 2),
            Event(Constants.CompanyEventTypes.NewListedCompany, 3)]);
        int marked = 0;
        _dbm.Setup(x => x.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (Result.SUCCESS, queue.Count > 0 ? queue.Dequeue() : null));
        _dbm.Setup(x => x.MarkInstrumentEventAsProcessed(It.IsAny<InstrumentEventExDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.SUCCESS)
            .Callback(() => Interlocked.Increment(ref marked));

        await RunUntil(() => Volatile.Read(ref marked) == 3, TimeSpan.FromSeconds(3));

        _ = Volatile.Read(ref marked).Should().Be(3);
    }

    [Fact]
    public async Task EventThatFails_IsRetriedAfterTheInterval_NotInATightLoop() {
        // A transient failure leaves the event unprocessed, so the next check returns it again.
        int checks = 0;
        _dbm.Setup(x => x.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (Result.SUCCESS, Event(Constants.CompanyEventTypes.RawDataChanged)))
            .Callback(() => Interlocked.Increment(ref checks));
        _dbm.Setup(x => x.GetCurrentInstrumentReports(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("db down"), (IReadOnlyList<CurrentInstrumentRawDataReportDto>)Array.Empty<CurrentInstrumentRawDataReportDto>()));

        await RunUntil(() => false, TimeSpan.FromSeconds(1));

        _ = Volatile.Read(ref checks).Should().Be(1);
        VerifyMarkProcessed(Times.Never());
    }

    [Fact]
    public async Task EventThatCannotBeMarkedProcessed_IsRetriedAfterTheInterval_NotInATightLoop() {
        int checks = 0;
        _dbm.Setup(x => x.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (Result.SUCCESS, Event(Constants.CompanyEventTypes.NewListedCompany)))
            .Callback(() => Interlocked.Increment(ref checks));
        ExpectMarkProcessedReturns(Result.SetFailure("db down"));

        await RunUntil(() => false, TimeSpan.FromSeconds(1));

        _ = Volatile.Read(ref checks).Should().Be(1);
    }

    #endregion
}
