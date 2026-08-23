using System.Collections.Generic;
using tsx_aggregator.models;

namespace tsx_aggregator;

/// <summary>
/// The outcome of diffing a freshly fetched directory against the registry.
/// </summary>
/// <param name="NewInstruments">Instruments that have never had a row in the database.</param>
/// <param name="RevivedInstruments">
/// Instruments that were previously obsoleted and are listed again. These carry the
/// instrument_id of their existing row, so their already-scraped reports stay attached.
/// </param>
/// <param name="ObsoletedInstruments">Instruments that are no longer listed.</param>
internal record UpdateRegistryResults(
    IList<InstrumentDto> NewInstruments,
    IList<InstrumentDto> RevivedInstruments,
    IList<InstrumentDto> ObsoletedInstruments) {
    public bool IsEmpty => NewInstruments.Count == 0 && RevivedInstruments.Count == 0 && ObsoletedInstruments.Count == 0;
}
