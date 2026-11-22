using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// Marking instrument events processed, against real PostgreSQL (code review P9). The UPDATE matched every event of the
/// instrument and type, so a raw-data change that arrived while the company was being aggregated was marked processed
/// with the earlier one, and the change was lost until the next scrape.
/// </summary>
public sealed class InstrumentEventSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private const long InstrumentId = 42;
    private static readonly DateTime Earlier = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddMinutes(1);

    private async Task Reset() {
        _ = await db.Scalar("DELETE FROM instrument_events");
        _ = await db.Scalar("INSERT INTO instruments (instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date)"
            + " VALUES (@id, 'TSX', 'ABC', 'ABC Inc.', 'ABC', 'ABC Common', now()) ON CONFLICT DO NOTHING", ("id", InstrumentId));
    }

    private async Task AddRawDataChangedEvent(DateTime eventDateUtc) =>
        _ = await db.Scalar("INSERT INTO instrument_events (instrument_id, event_date, event_type, is_processed) VALUES (@id, @date, 4, false)",
            ("id", InstrumentId), ("date", eventDateUtc));

    private async Task<long> PendingEvents() =>
        (long)(await db.Scalar("SELECT count(*) FROM instrument_events WHERE instrument_id = @id AND NOT is_processed", ("id", InstrumentId)))!;

    private static InstrumentEventExDto Handled(DateTime eventDateUtc) =>
        new(new InstrumentEventDto(InstrumentId, new DateTimeOffset(eventDateUtc), (int)Constants.CompanyEventTypes.RawDataChanged, IsProcessed: true),
            "ABC", "ABC Inc.", Constants.TsxExchange, 1m, 100);

    [PostgresFact]
    public async Task MarkingAnEventProcessed_LeavesALaterEventOfTheSameTypePending() {
        await Reset();
        await AddRawDataChangedEvent(Earlier);
        await AddRawDataChangedEvent(Later); // arrived while the earlier one was being aggregated

        Result res = await db.Dbm.MarkInstrumentEventAsProcessed(Handled(Earlier), CancellationToken.None);

        res.Success.Should().BeTrue();
        (await PendingEvents()).Should().Be(1, "the later change has not been aggregated yet");
    }

    [PostgresFact]
    public async Task MarkingAnEventProcessed_AlsoMarksEarlierPendingEventsOfTheSameType() {
        // Changes up to the handled event were all in the data the Aggregator read, so one aggregation covers them.
        await Reset();
        await AddRawDataChangedEvent(Earlier);
        await AddRawDataChangedEvent(Later);

        _ = await db.Dbm.MarkInstrumentEventAsProcessed(Handled(Later), CancellationToken.None);

        (await PendingEvents()).Should().Be(0);
    }

    [PostgresFact]
    public async Task InsertingAProcessedReport_DoesNotMarkEventsByItself() {
        // The Aggregator marks the handled event right after a successful insert; the insert's own unconditional
        // UPDATE of every RawDataChanged event for the instrument was the same bug a second time.
        await Reset();
        await AddRawDataChangedEvent(Later);

        Result res = await db.Dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(InstrumentId, "{}", DateTimeOffset.UtcNow, null), CancellationToken.None);

        res.Success.Should().BeTrue();
        (await PendingEvents()).Should().Be(1);
    }
}
