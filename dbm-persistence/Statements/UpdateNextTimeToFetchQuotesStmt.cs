using System;
using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal sealed class UpdateNextTimeToFetchQuotesStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE state_fsm_state SET next_fetch_stock_quote_time = @next_fetch_stock_quote_time";

    private readonly DateTime _nextFetchStockQuoteTime;

    public UpdateNextTimeToFetchQuotesStmt(DateTime nextFetchStockQuoteTime) 
        : base(sql, nameof(UpdateNextTimeToFetchQuotesStmt)) {
        _nextFetchStockQuoteTime = nextFetchStockQuoteTime;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<DateTime>("next_fetch_stock_quote_time", _nextFetchStockQuoteTime)
        };
    }
}
