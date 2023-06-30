using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetProcessedStockDataByExchangeAndInstrumentSymbolStmt : QueryDbStmtBase {
    private static readonly string sql = "SELECT i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date, pir.created_date, COUNT(ir.instrument_report_id)"
        + " FROM instruments i JOIN processed_instrument_reports pir ON i.instrument_id = pir.instrument_id"
        + " JOIN instrument_reports ir ON i.instrument_id = ir.instrument_id"
        + " WHERE i.obsoleted_date IS NULL"
        + " AND pir.obsoleted_date IS NULL"
        + " AND i.exchange = @exchange"
        + " AND i.instrument_symbol = @instrument_symbol"
        + " AND ir.report_type = " + (int)Constants.ReportTypes.CashFlow
        + " AND ir.report_period_type = " + (int)Constants.ReportPeriodTypes.Annual
        + " AND ir.is_current = TRUE"
        + " GROUP BY i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date, pir.created_date";

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

    protected override void ClearResults() {
        _processedInstrumentReportDto = null;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<string>("exchange", _exchange),
            new NpgsqlParameter<string>("instrument_symbol", _instrumentSymbol)
        };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _processedInstrumentReportDto = new ProcessedFullInstrumentReportDto(
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
        return true;
    }
}
