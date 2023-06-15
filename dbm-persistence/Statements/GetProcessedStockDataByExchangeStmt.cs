using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetProcessedStockDataByExchangeStmt : QueryDbStmtBase {
    private static readonly string sql = "SELECT i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date, pir.created_date, COUNT(ir.instrument_report_id)"
        + " FROM instruments i JOIN processed_instrument_reports pir ON i.instrument_id = pir.instrument_id"
        + " JOIN instrument_reports ir ON i.instrument_id = ir.instrument_id"
        + " WHERE i.obsoleted_date IS NULL"
        + " AND pir.obsoleted_date IS NULL"
        + " AND i.exchange = @exchange"
        + " AND ir.report_type = " + (int)Constants.ReportTypes.CashFlow
        + " AND ir.report_period_type = " + (int)Constants.ReportPeriodTypes.Annual
        + " AND ir.is_current = TRUE"
        + " GROUP BY i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date, pir.created_date";

    // Inputs
    private readonly string _exchange;

    // Results
    private readonly List<ProcessedFullInstrumentReportDto> _processedInstrumentReportDtoList;

    public GetProcessedStockDataByExchangeStmt(string exchange) : base(sql, nameof(GetProcessedStockDataByExchangeStmt)) {
        _exchange = exchange;
        _processedInstrumentReportDtoList = new();
    }

    public IReadOnlyList<ProcessedFullInstrumentReportDto> ProcessedInstrumentReports => _processedInstrumentReportDtoList;

    protected override void ClearResults() {
        _processedInstrumentReportDtoList.Clear();
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<string>("exchange", _exchange)
        };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        var i = new ProcessedFullInstrumentReportDto(
            InstrumentId: reader.GetInt64(0),
            CompanySymbol: reader.GetString(1),
            CompanyName: reader.GetString(2),
            InstrumentSymbol: reader.GetString(3),
            InstrumentName: reader.GetString(4),
            SerializedReport: reader.GetString(5),
            InstrumentCreatedDate: reader.GetDateTime(6).EnsureUtc(),
            InstrumentObsoletedDate: null,
            ReportCreatedDate: reader.GetDateTime(7).EnsureUtc(),
            ReportObsoletedDate: null,
            NumAnnualCashFlowReports: reader.GetInt32(8));
        _processedInstrumentReportDtoList.Add(i);
        return true;
    }
}
