using System;
using System.Collections.Generic;
using Npgsql;

using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal sealed class IgnoreRawDataReportStmt : NonQueryBatchedDbStmtBase {
    internal const string ignoreSql = "UPDATE instrument_reports"
        + " SET ignore_report = true, is_current = false, check_manually = false"
        + " WHERE instrument_report_id = ANY(@instrument_report_ids)";

    internal const string keepSql = "UPDATE instrument_reports"
        + " SET ignore_report = false, is_current = true, check_manually = false"
        + " WHERE instrument_report_id = @keep_report_id";

    // Inputs
    private readonly long _instrumentId;
    private readonly long _instrumentReportIdToKeep;
    private readonly List<long> _instrumentReportIdsToIgnore;

    public IgnoreRawDataReportStmt(long instrumentId, long instrumentReportIdToKeep, IReadOnlyCollection<long> instrumentReportIdsToIgnore)
        : base(nameof(IgnoreRawDataReportStmt)) {
        _instrumentId = instrumentId;
        _instrumentReportIdToKeep = instrumentReportIdToKeep;
        _instrumentReportIdsToIgnore = new List<long>(instrumentReportIdsToIgnore);

        DateTime eventTime = DateTime.UtcNow;

        AddCommandToBatch(ignoreSql, new NpgsqlParameter[] {
            new NpgsqlParameter<long[]>("instrument_report_ids", _instrumentReportIdsToIgnore.ToArray())
        });

        AddCommandToBatch(keepSql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("keep_report_id", _instrumentReportIdToKeep)
        });

        AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentId),
            new NpgsqlParameter<DateTime>("event_date", eventTime),
            new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.RawDataChanged),
            new NpgsqlParameter<bool>("is_processed", false)
        });
    }
}
