using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>
/// GetNextInstrumentEventStmt against real PostgreSQL (code review R58, R59: the B14 regression tests in
/// NextInstrumentEventTests only covered the in-memory fake). The oldest unprocessed event comes first; a
/// RawDataChanged event needs a listed instrument and a current price row, while listing, updated and delisting events
/// are returned regardless, with price and shares 0 when there is no price row (decision 25).
/// </summary>
public sealed class NextInstrumentEventSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private static readonly DateTime T0 = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    private static InstrumentDto Instrument(long id, string symbol) =>
        new(id, TsxExchange, symbol, symbol + " Inc.", symbol, symbol + " Common", DateTimeOffset.UtcNow, null);

    private async Task<InstrumentEventExDto?> Next() {
        (Result res, InstrumentEventExDto? evt) = await db.Dbm.GetNextInstrumentEvent(CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return evt;
    }

    [PostgresFact]
    public async Task NoPendingEvents_ReturnsNull() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddPrice(db, 1, 12.5m, 1_000_000);
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged, isProcessed: true);

        (await Next()).Should().BeNull();
    }

    [PostgresFact]
    public async Task OldestUnprocessedEvent_IsReturnedFirst_WithItsInstrumentAndPrice() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddInstrument(db, 2, "BBB");
        await SqlTestRows.AddPrice(db, 1, 12.5m, 1_000_000);
        await SqlTestRows.AddPrice(db, 2, 3.25m, 40_000_000);
        await SqlTestRows.AddEvent(db, 1, T0.AddMinutes(-10), CompanyEventTypes.RawDataChanged, isProcessed: true);
        await SqlTestRows.AddEvent(db, 1, T0.AddMinutes(5), CompanyEventTypes.RawDataChanged); // inserted before the older one
        await SqlTestRows.AddEvent(db, 2, T0, CompanyEventTypes.RawDataChanged);

        InstrumentEventExDto? evt = await Next();

        evt.Should().NotBeNull();
        evt!.InstrumentId.Should().Be(2);
        evt.EventDate.Should().Be(new DateTimeOffset(T0));
        evt.EventType.Should().Be((int)CompanyEventTypes.RawDataChanged);
        evt.IsProcessed.Should().BeFalse();
        evt.InstrumentSymbol.Should().Be("BBB");
        evt.InstrumentName.Should().Be("BBB Common Shares");
        evt.Exchange.Should().Be(TsxExchange);
        evt.PricePerShare.Should().Be(3.25m);
        evt.NumShares.Should().Be(40_000_000);
    }

    [PostgresFact]
    public async Task RawDataChanged_WithoutPriceRow_IsSkipped() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "NOPRICE");
        await SqlTestRows.AddInstrument(db, 2, "PRICED");
        await SqlTestRows.AddPrice(db, 2, 7m, 2_000_000);
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged);
        await SqlTestRows.AddEvent(db, 2, T0.AddMinutes(1), CompanyEventTypes.RawDataChanged);

        (await Next())!.InstrumentId.Should().Be(2, "a report cannot be rebuilt without a price");
    }

    [PostgresFact]
    public async Task RawDataChanged_WithOnlyAnObsoletedPriceRow_IsSkipped() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddPrice(db, 1, 9m, 1_000_000, obsoletedUtc: T0.AddDays(-1));
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged);

        (await Next()).Should().BeNull();
    }

    [PostgresFact]
    public async Task RawDataChanged_UsesTheCurrentPriceRow_NotAnObsoletedOne() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "AAA");
        await SqlTestRows.AddPrice(db, 1, 9m, 1_000_000, obsoletedUtc: T0.AddDays(-1));
        await SqlTestRows.AddPrice(db, 1, 11.75m, 1_100_000);
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged);

        InstrumentEventExDto? evt = await Next();

        evt!.PricePerShare.Should().Be(11.75m);
        evt.NumShares.Should().Be(1_100_000);
    }

    [PostgresFact]
    public async Task RawDataChanged_ForDelistedInstrument_IsSkipped() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "GONE", obsoletedUtc: T0.AddDays(-1));
        await SqlTestRows.AddPrice(db, 1, 4m, 5_000_000);
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged);

        (await Next()).Should().BeNull();
    }

    [PostgresFact]
    public async Task NewListing_WithoutPriceRow_IsReturned_WithZeroPriceAndShares() {
        // Mirrors NextInstrumentEventTests.NewListing_WithoutPriceRow_IsReturned (in-memory).
        await SqlTestRows.ClearAll(db);
        _ = await db.Dbm.UpdateInstrumentList([Instrument(1, "NEW")], [], [], CancellationToken.None);

        InstrumentEventExDto? evt = await Next();

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be((int)CompanyEventTypes.NewListedCompany);
        evt.InstrumentSymbol.Should().Be("NEW");
        evt.PricePerShare.Should().Be(0m);
        evt.NumShares.Should().Be(0);
    }

    [PostgresFact]
    public async Task UpdatedListing_WithoutPriceRow_IsReturned() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "UPD");
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.UpdatedListedCompany);

        InstrumentEventExDto? evt = await Next();

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be((int)CompanyEventTypes.UpdatedListedCompany);
        evt.PricePerShare.Should().Be(0m);
    }

    [PostgresFact]
    public async Task DelistingEvent_ForDelistedInstrument_IsReturned() {
        // Mirrors NextInstrumentEventTests.DelistingEvent_ForDelistedInstrument_IsReturned (in-memory).
        await SqlTestRows.ClearAll(db);
        InstrumentDto instrument = Instrument(1, "OLD");
        _ = await db.Dbm.UpdateInstrumentList([instrument], [], [], CancellationToken.None);
        InstrumentEventExDto? listing = await Next();
        _ = await db.Dbm.MarkInstrumentEventAsProcessed(
            listing! with { InstrumentEvent = listing.InstrumentEvent with { IsProcessed = true } }, CancellationToken.None);
        _ = await db.Dbm.UpdateInstrumentList([], [], [instrument], CancellationToken.None);

        InstrumentEventExDto? evt = await Next();

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be((int)CompanyEventTypes.ObsoletedListedCompany);
        evt.InstrumentId.Should().Be(1);
        evt.NumShares.Should().Be(0);
    }

    [PostgresFact]
    public async Task SkippedRawDataChanged_DoesNotHideALaterListingEvent() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "NOPRICE");
        await SqlTestRows.AddInstrument(db, 2, "NEW");
        await SqlTestRows.AddEvent(db, 1, T0, CompanyEventTypes.RawDataChanged);
        await SqlTestRows.AddEvent(db, 2, T0.AddHours(1), CompanyEventTypes.NewListedCompany);

        InstrumentEventExDto? evt = await Next();

        evt!.InstrumentId.Should().Be(2);
        evt.EventType.Should().Be((int)CompanyEventTypes.NewListedCompany);
    }
}
