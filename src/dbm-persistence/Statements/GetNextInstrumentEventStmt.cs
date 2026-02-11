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

    private static int _instrumentIdIndex = -1;
    private static int _eventDateIndex = -1;
    private static int _eventTypeIndex = -1;
    private static int _isProcessedIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _exchangeIndex = -1;
    private static int _pricePerShareIndex = -1;
    private static int _numSharesIndex = -1;

    public InstrumentEventExDto? InstrumentEventDto { get; private set; }

    public GetNextInstrumentEventStmt() : base(sql, nameof(GetNextInstrumentEventStmt)) { }

    protected override void ClearResults() => InstrumentEventDto = null;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentIdIndex == -1) {
            _instrumentIdIndex = reader.GetOrdinal("instrument_id");
            _eventDateIndex = reader.GetOrdinal("event_date");
            _eventTypeIndex = reader.GetOrdinal("event_type");
            _isProcessedIndex = reader.GetOrdinal("is_processed");
            _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
            _instrumentNameIndex = reader.GetOrdinal("instrument_name");
            _exchangeIndex = reader.GetOrdinal("exchange");
            _pricePerShareIndex = reader.GetOrdinal("price_per_share");
            _numSharesIndex = reader.GetOrdinal("num_shares");
        }
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        InstrumentEventDto = new(
            new InstrumentEventDto(
                reader.GetInt64(_instrumentIdIndex),    // InstrumentId
                reader.GetDateTime(_eventDateIndex),    // EventDate
                reader.GetInt32(_eventTypeIndex),       // EventType
                reader.GetBoolean(_isProcessedIndex)),  // IsProcessed
            reader.GetString(_instrumentSymbolIndex),   // InstrumentSymbol
            reader.GetString(_instrumentNameIndex),     // InstrumentName
            reader.GetString(_exchangeIndex),           // Exchange
            reader.GetDecimal(_pricePerShareIndex),     // Price per share
            reader.GetInt64(_numSharesIndex));          // Num shares
        return false;
    }
}
