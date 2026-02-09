using System.Collections.Generic;

namespace tsx_aggregator.models;

public record InstrumentInfo(
    long InstrumentId,
    string Exchange,
    string CompanySymbol,
    string InstrumentSymbol,
    string CompanyName,
    string InstrumentName);

public record PagedInstrumentInfoReports(
    PagingData PagingData,
    IEnumerable<InstrumentInfo> Instruments);
