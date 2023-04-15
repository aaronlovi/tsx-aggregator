using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class UpdateInstrumentEventStmt : NonQueryDbStmtBase {
    public const string sql = "UPDATE instrument_events"
        + " SET is_processed = @is_processed"
        + " WHERE instrument_id = @instrument_id"
        + " AND event_type = @event_type";

    private readonly InstrumentEventDto _instrumentEventDto;

    public UpdateInstrumentEventStmt(InstrumentEventDto instrumentEventDto) : base(sql, nameof(UpdateInstrumentEventStmt)) {
        _instrumentEventDto = instrumentEventDto;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<bool>("is_processed", _instrumentEventDto.IsProcessed),
            new NpgsqlParameter<long>("instrument_id", _instrumentEventDto.InstrumentId),
            new NpgsqlParameter<int>("event_type", _instrumentEventDto.EventType)
        };
    }
}
