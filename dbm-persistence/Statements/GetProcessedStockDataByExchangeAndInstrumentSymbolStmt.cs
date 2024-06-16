using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetProcessedStockDataByExchangeAndInstrumentSymbolStmt : QueryDbStmtBase {
    private static readonly string sql = "SELECT i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date as instrument_created_date, pir.created_date as report_created_date,"
        + " COUNT(ir.instrument_report_id) as num_reports"
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

    private static int _instrumentIdIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _companyNameIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _reportJsonIndex = -1;
    private static int _instrumentCreatedDateIndex = -1;
    private static int _reportCreatedDateIndex = -1;
    private static int _numAnnualCashFlowReportsIndex = -1;

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
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<string>("exchange", _exchange),
            new NpgsqlParameter<string>("instrument_symbol", _instrumentSymbol)
        };
    }

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        if (_instrumentIdIndex != -1)
            return;

        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _companySymbolIndex = reader.GetOrdinal("company_symbol");
        _companyNameIndex = reader.GetOrdinal("company_name");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
        _instrumentNameIndex = reader.GetOrdinal("instrument_name");
        _reportJsonIndex = reader.GetOrdinal("report_json");
        _instrumentCreatedDateIndex = reader.GetOrdinal("instrument_created_date");
        _reportCreatedDateIndex = reader.GetOrdinal("report_created_date");
        _numAnnualCashFlowReportsIndex = reader.GetOrdinal("num_reports");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _processedInstrumentReportDto = new ProcessedFullInstrumentReportDto(
            InstrumentId: reader.GetInt64(_instrumentIdIndex),
            CompanySymbol: reader.GetString(_companySymbolIndex),
            CompanyName: reader.GetString(_companyNameIndex),
            InstrumentSymbol: reader.GetString(_instrumentSymbolIndex),
            InstrumentName: reader.GetString(_instrumentNameIndex),
            SerializedReport: reader.GetString(_reportJsonIndex),
            InstrumentCreatedDate: reader.GetDateTime(_instrumentCreatedDateIndex).EnsureUtc(),
            InstrumentObsoletedDate: null,
            ReportCreatedDate: reader.GetDateTime(_reportCreatedDateIndex).EnsureUtc(),
            ReportObsoletedDate: null,
            NumAnnualCashFlowReports: reader.GetInt32(_numAnnualCashFlowReportsIndex));
        return true;
    }
}
