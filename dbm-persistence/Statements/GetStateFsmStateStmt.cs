using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetStateFsmStateStmt : QueryDbStmtBase {
    private const string sql = "SELECT next_fetch_directory_time, next_fetch_instrument_data_time, prev_company_symbol, prev_instrument_symbol,"
        + " next_fetch_stock_quote_time"
        + " FROM state_fsm_state";

    private StateFsmState _stateFsmState;

    private static int _nextFetchDirectoryTimeIndex = -1;
    private static int _nextFetchInstrumentDataTimeIndex = -1;
    private static int _prevCompanySymbolIndex = -1;
    private static int _prevInstrumentSymbolIndex = -1;
    private static int _nextFetchStockQuoteTimeIndex = -1;

    public GetStateFsmStateStmt() : base(sql, nameof(GetStateFsmStateStmt)) =>
        _stateFsmState = new(null, null, null, InstrumentKey.Empty);

    public StateFsmState Results => _stateFsmState;

    protected override void ClearResults() => 
        _stateFsmState = new(null, null, null, InstrumentKey.Empty);

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        Array.Empty<NpgsqlParameter>();

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        if (_nextFetchDirectoryTimeIndex != -1)
            return;

        _nextFetchDirectoryTimeIndex = reader.GetOrdinal("next_fetch_directory_time");
        _nextFetchInstrumentDataTimeIndex = reader.GetOrdinal("next_fetch_instrument_data_time");
        _prevCompanySymbolIndex = reader.GetOrdinal("prev_company_symbol");
        _prevInstrumentSymbolIndex = reader.GetOrdinal("prev_instrument_symbol");
        _nextFetchStockQuoteTimeIndex = reader.GetOrdinal("next_fetch_stock_quote_time");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _stateFsmState.NextFetchDirectoryTime = reader.GetDateTime(_nextFetchDirectoryTimeIndex);
        _stateFsmState.NextFetchInstrumentDataTime = reader.GetDateTime(_nextFetchInstrumentDataTimeIndex);
        _stateFsmState.PrevInstrumentKey = new InstrumentKey(
            reader.GetString(_prevCompanySymbolIndex),
            reader.GetString(_prevInstrumentSymbolIndex),
            Constants.TsxExchange);
        _stateFsmState.NextFetchQuotesTime = reader.GetDateTime(_nextFetchStockQuoteTimeIndex);
        return false; // Since we only expect one row, we return false to stop processing further rows.
    }
}
