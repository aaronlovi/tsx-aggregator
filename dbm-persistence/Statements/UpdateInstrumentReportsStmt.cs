using System;
using Npgsql;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal class UpdateInstrumentReportsStmt : NonQueryBatchedDbStmtBase {
    private const string InsertSql = "INSERT INTO instrument_reports"
        + " (instrument_report_id, instrument_id, report_type, report_period_type, report_json, report_date,"
        + " created_date, obsoleted_date, is_current, check_manually)"
        + " VALUES (@instrument_report_id, @instrument_id, @report_type, @report_period_type, @report_json, @report_date,"
        + " @created_date, @obsoleted_date, true, @check_manually)";

    private const string ObsoleteSql = "UPDATE instrument_reports"
        + " SET is_current = false, obsoleted_date = @obsoleted_date"
        + " WHERE instrument_report_id = @instrument_report_id";

    private const string InsertPricesSql = "INSERT INTO instrument_prices"
        + " (instrument_id, price_per_share, num_shares, created_date, obsoleted_date)"
        + " VALUES (@instrument_id, @price_per_share, @num_shares, @created_date, @obsoleted_date)";

    private const string ObsoletePricesSql = "UPDATE instrument_prices"
        + " SET obsoleted_date = now()"
        + " WHERE instrument_id = @instrument_id";

    // Inputs
    private readonly RawFinancialsDelta _rawFinancialsDelta;

    public UpdateInstrumentReportsStmt(RawFinancialsDelta rawFinancialsDelta)
        : base(nameof(UpdateInstrumentReportsStmt)) {
        _rawFinancialsDelta = new RawFinancialsDelta(rawFinancialsDelta);

        DateTime utcNow = DateTime.UtcNow;

        foreach (var obsoletedReport in _rawFinancialsDelta.InstrumentReportsToObsolete) {
            AddCommandToBatch(ObsoleteSql, new NpgsqlParameter[] {
                new NpgsqlParameter<DateTime>("obsoleted_date", utcNow),
                new NpgsqlParameter<long>("instrument_report_id", obsoletedReport.InstrumentReportId),
            });
        }

        NumReportsToInsert = 0;
        NumReportsToCheckManually = 0;

        foreach (var newReport in _rawFinancialsDelta.InstrumentReportsToInsert) {
            AddCommandToBatch(InsertSql, new NpgsqlParameter[] {
                new NpgsqlParameter<long>("instrument_report_id", newReport.InstrumentReportId),
                new NpgsqlParameter<long>("instrument_id", newReport.InstrumentId),
                new NpgsqlParameter<int>("report_type", newReport.ReportType),
                new NpgsqlParameter<int>("report_period_type", newReport.ReportPeriodType),
                new NpgsqlParameter<string>("report_json", newReport.ReportJson),
                new NpgsqlParameter<DateTime>("report_date", newReport.ReportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                new NpgsqlParameter<DateTime>("created_date", utcNow),
                DbUtils.CreateNullableDateTimeParam("obsoleted_date", null),
                new NpgsqlParameter<bool>("is_current", true),
                new NpgsqlParameter<bool>("check_manually", newReport.CheckManually)
            });

            NumReportsToInsert += newReport.CheckManually ? 0 : 1;
            NumReportsToCheckManually += newReport.CheckManually ? 1 : 0;
        }

        if (NumReportsToInsert > 0 || NumReportsToObsolete > 0) {
            AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
                new NpgsqlParameter<long>("instrument_id", InstrumentId),
                new NpgsqlParameter<DateTime>("event_date", utcNow),
                new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.RawDataChanged),
                new NpgsqlParameter<bool>("is_processed", false)
            });
        }

        AddCommandToBatch(ObsoletePricesSql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", InstrumentId)
        });

        AddCommandToBatch(InsertPricesSql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", InstrumentId),
            new NpgsqlParameter<decimal>("price_per_share", _rawFinancialsDelta.PricePerShare),
            new NpgsqlParameter<long>("num_shares", (long)_rawFinancialsDelta.NumShares),
            new NpgsqlParameter<DateTime>("created_date", utcNow),
            DbUtils.CreateNullableDateTimeParam("obsoleted_date", null)
        });
    }

    public long InstrumentId => _rawFinancialsDelta.InstrumentId;

    public int NumReportsToInsert { get; private set; }

    public int NumReportsToCheckManually { get; private set; }

    public int NumReportsToObsolete => _rawFinancialsDelta.InstrumentReportsToObsolete.Count;
}
