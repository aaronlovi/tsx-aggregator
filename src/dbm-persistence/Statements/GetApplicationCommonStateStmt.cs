using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetApplicationCommonStateStmt : QueryDbStmtBase {
    private const string sql = "SELECT next_fetch_directory_time, next_fetch_instrument_data_time, prev_company_symbol, prev_instrument_symbol,"
        + " next_fetch_stock_quote_time"
        + " FROM state_fsm_state";

    private ApplicationCommonState _rawCollectorState;

    private static int _nextFetchDirectoryTimeIndex = -1;
    private static int _nextFetchInstrumentDataTimeIndex = -1;
    private static int _prevCompanySymbolIndex = -1;
    private static int _prevInstrumentSymbolIndex = -1;
    private static int _nextFetchStockQuoteTimeIndex = -1;

    public GetApplicationCommonStateStmt() : base(sql, nameof(GetApplicationCommonStateStmt)) => _rawCollectorState = new();

    public ApplicationCommonState Results => _rawCollectorState;

    protected override void ClearResults() => _rawCollectorState = new();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        Array.Empty<NpgsqlParameter>();

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_nextFetchDirectoryTimeIndex != -1)
            return;

        _nextFetchDirectoryTimeIndex = reader.GetOrdinal("next_fetch_directory_time");
        _nextFetchInstrumentDataTimeIndex = reader.GetOrdinal("next_fetch_instrument_data_time");
        _prevCompanySymbolIndex = reader.GetOrdinal("prev_company_symbol");
        _prevInstrumentSymbolIndex = reader.GetOrdinal("prev_instrument_symbol");
        _nextFetchStockQuoteTimeIndex = reader.GetOrdinal("next_fetch_stock_quote_time");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _rawCollectorState.NextFetchDirectoryTime = reader.GetDateTime(_nextFetchDirectoryTimeIndex);
        _rawCollectorState.NextFetchInstrumentDataTime = reader.GetDateTime(_nextFetchInstrumentDataTimeIndex);
        _rawCollectorState.PrevInstrumentKey = new InstrumentKey(
            reader.GetString(_prevCompanySymbolIndex),
            reader.GetString(_prevInstrumentSymbolIndex),
            Constants.TsxExchange);
        _rawCollectorState.NextFetchQuotesTime = reader.GetDateTime(_nextFetchStockQuoteTimeIndex);
        return false; // Since we only expect one row, we return false to stop processing further rows.
    }
}
