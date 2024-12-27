using System.Collections.Generic;
using tsx_aggregator.models;

namespace tsx_aggregator;

internal record UpdateRegistryResults(
    IList<InstrumentDto> NewInstruments,
    IList<InstrumentDto> ObsoletedInstruments) {
    public bool IsEmpty => NewInstruments.Count == 0 && ObsoletedInstruments.Count == 0;
}
