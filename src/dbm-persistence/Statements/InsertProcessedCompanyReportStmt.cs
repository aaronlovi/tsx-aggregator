using System;
using Npgsql;
using tsx_aggregator.models;

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
        AddCommandToBatch(ObsoleteOldRecordsSql, [
            new NpgsqlParameter<DateTime>("obsoleted_date", dto.ReportObsoletedDate?.UtcDateTime ?? DateTime.UtcNow),
            new NpgsqlParameter<long>("instrument_id", dto.InstrumentId)
        ]);

        AddCommandToBatch(InsertNewRecordSql, [
            new NpgsqlParameter<long>("instrument_id", dto.InstrumentId),
            new NpgsqlParameter<string>("report_json", dto.SerializedReport),
            new NpgsqlParameter<DateTime>("created_date", dto.ReportCreatedDate.UtcDateTime)
        ]);

        // The handled event is marked by the Aggregator right after this (MarkInstrumentEventAsProcessed), with its
        // date as the cutoff. This batch used to mark every RawDataChanged event of the instrument as well (review P9).
    }
}
