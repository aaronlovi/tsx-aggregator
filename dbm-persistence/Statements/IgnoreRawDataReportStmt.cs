using System;
using System.Collections.Generic;
using Npgsql;

using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal sealed class IgnoreRawDataReportStmt : NonQueryBatchedDbStmtBase {
    internal const string sql = "UPDATE instrument_reports"
        + " SET ignore_report = true, is_current = false, check_manually = false, obsoleted_date = @obsoleted_date"
        + " WHERE instrument_report_id = ANY(@instrument_report_ids)";

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

        AddCommandToBatch(sql, new NpgsqlParameter[] {
            new NpgsqlParameter<DateTime>("obsoleted_date", eventTime),
            new NpgsqlParameter<long[]>("instrument_report_ids", _instrumentReportIdsToIgnore.ToArray())
        });

        AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentId),
            new NpgsqlParameter<DateTime>("event_date", eventTime),
            new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.RawDataChanged),
            new NpgsqlParameter<bool>("is_processed", false)
        });
    }
}
