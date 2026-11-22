using System;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

/// <summary>
/// B14 (decision 25): listing and delisting events have nothing to aggregate, so the Aggregator must get them even
/// without a price row or for a delisted instrument. Before, they stayed unprocessed forever.
/// </summary>
public class NextInstrumentEventTests {
    private static IDbmService NewDbm() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<DbmInMemory>>(new NullLogger<DbmInMemory>());
        return new DbmInMemory(services.BuildServiceProvider());
    }

    private static InstrumentDto Instrument(long id, string symbol) =>
        new(id, "TSX", symbol, symbol + " Inc.", symbol, symbol + " Common", DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task NewListing_WithoutPriceRow_IsReturned() {
        IDbmService dbm = NewDbm();
        _ = await dbm.UpdateInstrumentList([Instrument(1, "NEW")], [], [], CancellationToken.None);

        (Result res, InstrumentEventExDto? evt) = await dbm.GetNextInstrumentEvent(CancellationToken.None);

        _ = res.Success.Should().BeTrue();
        _ = evt.Should().NotBeNull();
        _ = evt!.InstrumentEvent.EventType.Should().Be((int)Constants.CompanyEventTypes.NewListedCompany);
    }

    [Fact]
    public async Task DelistingEvent_ForDelistedInstrument_IsReturned() {
        IDbmService dbm = NewDbm();
        InstrumentDto instrument = Instrument(1, "OLD");
        _ = await dbm.UpdateInstrumentList([instrument], [], [], CancellationToken.None);
        (_, InstrumentEventExDto? listing) = await dbm.GetNextInstrumentEvent(CancellationToken.None);
        _ = await dbm.MarkInstrumentEventAsProcessed(
            listing! with { InstrumentEvent = listing.InstrumentEvent with { IsProcessed = true } }, CancellationToken.None);
        _ = await dbm.UpdateInstrumentList([], [], [instrument], CancellationToken.None);

        (Result res, InstrumentEventExDto? evt) = await dbm.GetNextInstrumentEvent(CancellationToken.None);

        _ = res.Success.Should().BeTrue();
        _ = evt.Should().NotBeNull();
        _ = evt!.InstrumentEvent.EventType.Should().Be((int)Constants.CompanyEventTypes.ObsoletedListedCompany);
    }
}
