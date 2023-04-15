using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class UpdateStateFsmStateStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE state_fsm_state"
        + " SET next_fetch_directory_time = @next_fetch_directory_time,"
        + " next_fetch_instrument_data_time = @next_fetch_instrument_data_time,"
        + " prev_company_symbol = @prev_company_symbol,"
        + " prev_instrument_symbol = @prev_instrument_symbol";

    private readonly StateFsmState _stateFsmState;

    public UpdateStateFsmStateStmt(StateFsmState stateFsmState) 
        : base(sql, nameof(UpdateStateFsmStateStmt)) {
        _stateFsmState = new(stateFsmState);
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<DateTime>("next_fetch_directory_time", _stateFsmState.NextFetchDirectoryTime.GetValueOrDefault()),
            new NpgsqlParameter<DateTime>("next_fetch_instrument_data_time", _stateFsmState.NextFetchInstrumentDataTime.GetValueOrDefault()),
            new NpgsqlParameter<string>("prev_company_symbol", _stateFsmState.PrevCompanyAndInstrumentSymbol.CompanySymbol),
            new NpgsqlParameter<string>("prev_instrument_symbol", _stateFsmState.PrevCompanyAndInstrumentSymbol.InstrumentSymbol)
        };
    }
}
