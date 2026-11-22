using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetProcessedStockDataByExchangeStmt : ProcessedStockDataStmtBase {
    private static readonly string sql = BuildSql(extraPredicate: "");

    // Inputs
    private readonly string _exchange;

    // Results
    private readonly List<ProcessedFullInstrumentReportDto> _processedInstrumentReportDtoList;

    public GetProcessedStockDataByExchangeStmt(string exchange) : base(sql, nameof(GetProcessedStockDataByExchangeStmt)) {
        _exchange = exchange;
        _processedInstrumentReportDtoList = [];
    }

    public IReadOnlyList<ProcessedFullInstrumentReportDto> ProcessedInstrumentReports => _processedInstrumentReportDtoList;

    protected override void ClearResults() => _processedInstrumentReportDtoList.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        new List<NpgsqlParameter> { new NpgsqlParameter<string>("exchange", _exchange) };

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _processedInstrumentReportDtoList.Add(ReadRow(reader));
        return true;
    }
}
