using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetProcessedStockDataByExchangeAndInstrumentSymbolStmt : ProcessedStockDataStmtBase {
    private static readonly string sql = BuildSql(extraPredicate: " AND i.instrument_symbol = @instrument_symbol");

    // Inputs
    private readonly string _exchange;
    private readonly string _instrumentSymbol;

    // Results
    private ProcessedFullInstrumentReportDto? _processedInstrumentReportDto;

    public GetProcessedStockDataByExchangeAndInstrumentSymbolStmt(string exchange, string instrumentSymbol)
        : base(sql, nameof(GetProcessedStockDataByExchangeAndInstrumentSymbolStmt)) {
        _exchange = exchange;
        _instrumentSymbol = instrumentSymbol;
    }

    public ProcessedFullInstrumentReportDto? ProcessedInstrumentReport => _processedInstrumentReportDto;

    protected override void ClearResults() => _processedInstrumentReportDto = null;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return [
            new NpgsqlParameter<string>("exchange", _exchange),
            new NpgsqlParameter<string>("instrument_symbol", _instrumentSymbol)
        ];
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _processedInstrumentReportDto = ReadRow(reader);
        return true;
    }
}
