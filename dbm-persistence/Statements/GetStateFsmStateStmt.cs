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

    public GetStateFsmStateStmt() : base(sql, nameof(GetStateFsmStateStmt)) {
        _stateFsmState = new(null, null, null, InstrumentKey.Empty);
    }

    public StateFsmState Results => _stateFsmState;

    protected override void ClearResults() {
        _stateFsmState = new(null, null, null, InstrumentKey.Empty);
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return Array.Empty<NpgsqlParameter>();
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _stateFsmState.NextFetchDirectoryTime = reader.GetDateTime(0);
        _stateFsmState.NextFetchInstrumentDataTime = reader.GetDateTime(1);
        _stateFsmState.PrevInstrumentKey = new(reader.GetString(2), reader.GetString(3), Constants.TsxExchange);
        _stateFsmState.NextFetchQuotesTime = reader.GetDateTime(4);
        return false;
    }
}
