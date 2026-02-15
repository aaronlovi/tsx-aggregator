using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class InsertInstrumentEventStmt : NonQueryDbStmtBase {
    internal const string sql = "INSERT INTO instrument_events (instrument_id, event_date, event_type, is_processed)"
        + " VALUES (@instrument_id, @event_date, @event_type, @is_processed)";

    // Inputs
    private InstrumentEventExDto _instrumentEventDto;

    public InsertInstrumentEventStmt(InstrumentEventExDto instrumentEventDto)
        : base(sql, nameof(InsertInstrumentEventStmt))
        => _instrumentEventDto = instrumentEventDto;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentEventDto.InstrumentId),
            new NpgsqlParameter<DateTime>("event_date", _instrumentEventDto.EventDate.UtcDateTime),
            new NpgsqlParameter<int>("event_type", _instrumentEventDto.EventType),
            new NpgsqlParameter<bool>("is_processed", _instrumentEventDto.IsProcessed)
        };
    }
}
