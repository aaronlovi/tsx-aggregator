using System;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class InsertProcessedCompanyReportStmt : NonQueryBatchedDbStmtBase {
    private const string InsertNewRecordSql = "INSERT INTO processed_instrument_reports"
        + " (instrument_id, report_json, created_date)"
        + " VALUES"
        + " (@instrument_id, @report_json, @created_date)";

    private const string ObsoleteOldRecordsSql = "UPDATE processed_instrument_reports"
        + " SET obsoleted_date = @obsoleted_date"
        + " WHERE instrument_id = @instrument_id"
        + " AND obsoleted_date IS null";

    public InsertProcessedCompanyReportStmt(ProcessedInstrumentReportDto dto) 
        : base(nameof(InsertProcessedCompanyReportStmt)) {
        AddCommandToBatch(ObsoleteOldRecordsSql, new NpgsqlParameter[] {
            new NpgsqlParameter<DateTime>("obsoleted_date", dto.ReportObsoletedDate?.DateTime ?? DateTime.UtcNow),
            new NpgsqlParameter<long>("instrument_id", dto.InstrumentId)
        });

        AddCommandToBatch(InsertNewRecordSql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", dto.InstrumentId),
            new NpgsqlParameter<string>("report_json", dto.SerializedReport),
            new NpgsqlParameter<DateTime>("created_date", dto.ReportCreatedDate.DateTime)
        });

        AddCommandToBatch(UpdateInstrumentEventStmt.sql, new NpgsqlParameter[] {
            new NpgsqlParameter<bool>("is_processed", true),
            new NpgsqlParameter<long>("instrument_id", dto.InstrumentId),
            new NpgsqlParameter<int>("event_type", (int)Constants.CompanyEventTypes.RawDataChanged),
            new NpgsqlParameter<DateTime>("event_date", dto.ReportCreatedDate.DateTime)
        });
    }
}
