using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class UpdateInstrumentEventStmt : NonQueryDbStmtBase {
    // Only events up to the handled one (review P9): their raw data was committed with them, before the Aggregator
    // read it. A later event of the same type stays pending, at worst costing one more aggregation; without the date,
    // a change that arrived during the aggregation was marked processed and lost until the next scrape.
    public const string sql = "UPDATE instrument_events"
        + " SET is_processed = @is_processed"
        + " WHERE instrument_id = @instrument_id"
        + " AND event_type = @event_type"
        + " AND event_date <= @event_date";

    private readonly InstrumentEventExDto _instrumentEventDto;

    public UpdateInstrumentEventStmt(InstrumentEventExDto instrumentEventDto)
        : base(sql, nameof(UpdateInstrumentEventStmt)) => 
        _instrumentEventDto = instrumentEventDto;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<bool>("is_processed", _instrumentEventDto.IsProcessed),
            new NpgsqlParameter<long>("instrument_id", _instrumentEventDto.InstrumentId),
            new NpgsqlParameter<int>("event_type", _instrumentEventDto.EventType),
            new NpgsqlParameter<DateTime>("event_date", _instrumentEventDto.InstrumentEvent.EventDate.UtcDateTime)
        };
    }
}
