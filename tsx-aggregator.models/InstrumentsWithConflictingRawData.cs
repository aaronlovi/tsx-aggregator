using System.Collections.Generic;

namespace tsx_aggregator.models;

public record InstrumentsWithConflictingRawData(
    PagingData PagingData,
    IEnumerable<InstrumentWithConflictingRawData> InstrumentWithConflictingRawData);
