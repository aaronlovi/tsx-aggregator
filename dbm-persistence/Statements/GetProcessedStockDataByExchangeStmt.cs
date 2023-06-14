using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;
internal sealed class GetProcessedStockDataByExchangeStmt : QueryDbStmtBase {
    private const string sql = "SELECT i.instrument_id, pir.report_json, pir.created_date"
        + " FROM instruments i JOIN processed_instrument_reports pir ON i.instrument_id = pir.instrument_id"
        + " WHERE i.obsoleted_date IS NULL"
        + " AND pir.obsoleted_date IS NULL"
        + " AND i.exchange = @exchange";
    
    // Inputs
    private readonly string _exchange;

    // Results
    private readonly List<ProcessedInstrumentReportDto> _processedInstrumentReportDtoList;

    public GetProcessedStockDataByExchangeStmt(string exchange) : base(sql, nameof(GetProcessedStockDataByExchangeStmt)) {
        _exchange = exchange;
        _processedInstrumentReportDtoList = new();
    }

    public IReadOnlyList<ProcessedInstrumentReportDto> ProcessedInstrumentReports => _processedInstrumentReportDtoList;

    protected override void ClearResults() {
        _processedInstrumentReportDtoList.Clear();
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<long>("exchange", _exchange)
        };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        var i = new ProcessedInstrumentReportDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2).EnsureUtc(),
            null);
        _processedInstrumentReportDtoList.Add(i);
        return true;
    }

}
