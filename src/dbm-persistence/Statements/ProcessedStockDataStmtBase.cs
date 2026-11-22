using System;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

/// <summary>
/// The shared query and row mapping of <see cref="GetProcessedStockDataByExchangeStmt"/> and
/// <see cref="GetProcessedStockDataByExchangeAndInstrumentSymbolStmt"/> (code review R55): the current processed report of
/// each listed instrument on <c>@exchange</c> that has current annual cash-flow reports, with their count and the latest
/// annual and quarterly report dates. A new column goes in <see cref="BuildSql"/> and <see cref="ReadRow"/> only.
/// </summary>
internal abstract class ProcessedStockDataStmtBase : QueryDbStmtBase {
    // Per instance, so concurrent statements never see a half-filled set of ordinals.
    private Ordinals _ordinals;

    protected ProcessedStockDataStmtBase(string sql, string className) : base(sql, className) { }

    /// <param name="extraPredicate">Added to the WHERE clause after the exchange filter, e.g. " AND i.x = @x"; empty for none.</param>
    protected static string BuildSql(string extraPredicate) =>
        "SELECT i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date as instrument_created_date, pir.created_date as report_created_date,"
        + " COUNT(ir.instrument_report_id) as num_reports,"
        + " (SELECT MAX(report_date) FROM instrument_reports WHERE instrument_id = i.instrument_id"
        + "    AND report_period_type = " + (int)Constants.ReportPeriodTypes.Annual + " AND is_current = TRUE) as last_annual_report_date,"
        + " (SELECT MAX(report_date) FROM instrument_reports WHERE instrument_id = i.instrument_id"
        + "    AND report_period_type = " + (int)Constants.ReportPeriodTypes.Quarterly + " AND is_current = TRUE) as last_quarterly_report_date,"
        + " i.last_scraped_date as last_updated_date"
        + " FROM instruments i JOIN processed_instrument_reports pir ON i.instrument_id = pir.instrument_id"
        + " JOIN instrument_reports ir ON i.instrument_id = ir.instrument_id"
        + " WHERE i.obsoleted_date IS NULL"
        + " AND pir.obsoleted_date IS NULL"
        + " AND i.exchange = @exchange"
        + extraPredicate
        + " AND ir.report_type = " + (int)Constants.ReportTypes.CashFlow
        + " AND ir.report_period_type = " + (int)Constants.ReportPeriodTypes.Annual
        + " AND ir.is_current = TRUE"
        + " GROUP BY i.instrument_id, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,"
        + " pir.report_json, i.created_date, pir.created_date, i.last_scraped_date";

    protected sealed override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);
        _ordinals = new Ordinals(reader);
    }

    protected ProcessedFullInstrumentReportDto ReadRow(NpgsqlDataReader reader) => new(
        InstrumentId: reader.GetInt64(_ordinals.InstrumentId),
        CompanySymbol: reader.GetString(_ordinals.CompanySymbol),
        CompanyName: reader.GetString(_ordinals.CompanyName),
        InstrumentSymbol: reader.GetString(_ordinals.InstrumentSymbol),
        InstrumentName: reader.GetString(_ordinals.InstrumentName),
        SerializedReport: reader.GetString(_ordinals.ReportJson),
        InstrumentCreatedDate: reader.GetDateTime(_ordinals.InstrumentCreatedDate).EnsureUtc(),
        InstrumentObsoletedDate: null,
        ReportCreatedDate: reader.GetDateTime(_ordinals.ReportCreatedDate).EnsureUtc(),
        ReportObsoletedDate: null,
        NumAnnualCashFlowReports: reader.GetInt32(_ordinals.NumReports),
        MostRecentAnnualReportDate: ReadNullableUtc(reader, _ordinals.LastAnnualReportDate),
        MostRecentQuarterlyReportDate: ReadNullableUtc(reader, _ordinals.LastQuarterlyReportDate),
        LastUpdatedDate: ReadNullableUtc(reader, _ordinals.LastUpdatedDate));

    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal).EnsureUtc();

    private readonly struct Ordinals {
        public readonly int InstrumentId;
        public readonly int CompanySymbol;
        public readonly int CompanyName;
        public readonly int InstrumentSymbol;
        public readonly int InstrumentName;
        public readonly int ReportJson;
        public readonly int InstrumentCreatedDate;
        public readonly int ReportCreatedDate;
        public readonly int NumReports;
        public readonly int LastAnnualReportDate;
        public readonly int LastQuarterlyReportDate;
        public readonly int LastUpdatedDate;

        public Ordinals(NpgsqlDataReader reader) {
            InstrumentId = reader.GetOrdinal("instrument_id");
            CompanySymbol = reader.GetOrdinal("company_symbol");
            CompanyName = reader.GetOrdinal("company_name");
            InstrumentSymbol = reader.GetOrdinal("instrument_symbol");
            InstrumentName = reader.GetOrdinal("instrument_name");
            ReportJson = reader.GetOrdinal("report_json");
            InstrumentCreatedDate = reader.GetOrdinal("instrument_created_date");
            ReportCreatedDate = reader.GetOrdinal("report_created_date");
            NumReports = reader.GetOrdinal("num_reports");
            LastAnnualReportDate = reader.GetOrdinal("last_annual_report_date");
            LastQuarterlyReportDate = reader.GetOrdinal("last_quarterly_report_date");
            LastUpdatedDate = reader.GetOrdinal("last_updated_date");
        }
    }
}
