using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetNextInstrumentEventStmt : QueryDbStmtBase {
    private const string sql = "SELECT ie.instrument_id, ie.event_date, ie.event_type, ie.is_processed,"
        + " i.instrument_symbol, i.instrument_name, i.exchange,"
        + " ip.price_per_share, ip.num_shares"
        + " FROM instrument_events ie"
        + " JOIN instruments i on ie.instrument_id = i.instrument_id"
        + " JOIN instrument_prices ip on ie.instrument_id = ip.instrument_id"
        + " WHERE ie.is_processed = FALSE"
        + " AND i.obsoleted_date is null"
        + " AND ip.obsoleted_date is null"
        + " ORDER BY event_date"
        + " LIMIT 1";

    public InstrumentEventExDto? InstrumentEventDto { get; private set; }

    public GetNextInstrumentEventStmt() : base(sql, nameof(GetNextInstrumentEventStmt)) { }

    protected override void ClearResults() {
        InstrumentEventDto = null;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return Array.Empty<NpgsqlParameter>();
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        InstrumentEventDto = new(
            new InstrumentEventDto(
                reader.GetInt64(0),     // InstrumentId
                reader.GetDateTime(1),  // EventDate
                reader.GetInt32(2),     // EventType
                reader.GetBoolean(3)),  // IsProcessed
            reader.GetString(4),        // InstrumentSymbol
            reader.GetString(5),        // InstrumentName
            reader.GetString(6),        // Exchange
            reader.GetDecimal(7),       // Price per share
            reader.GetInt64(8));        // Num shares
        return false;
    }
}
