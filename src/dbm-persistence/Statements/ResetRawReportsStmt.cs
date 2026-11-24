using System;
using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

/// <summary>
/// Marks raw reports as not current and queues one RawDataChanged (4) event per instrument that lost a report. One
/// statement, so it is all or nothing, and running it again changes nothing (review R31, tsx-ops merged-reports).
/// </summary>
internal sealed class ResetRawReportsStmt : NonQueryDbStmtBase {
    private const string sql = "WITH reset AS ("
        + " UPDATE instrument_reports SET is_current = FALSE, obsoleted_date = @now"
        + " WHERE instrument_report_id = ANY(@instrument_report_ids) AND is_current = TRUE"
        + " RETURNING instrument_id)"
        + " INSERT INTO instrument_events (instrument_id, event_date, event_type, is_processed)"
        + " SELECT DISTINCT instrument_id, @now, 4, FALSE FROM reset";

    private readonly long[] _instrumentReportIds;
    private readonly DateTime _nowUtc;

    public ResetRawReportsStmt(IReadOnlyCollection<long> instrumentReportIds, DateTimeOffset now)
        : base(sql, nameof(ResetRawReportsStmt)) {
        _instrumentReportIds = [.. instrumentReportIds];
        _nowUtc = now.UtcDateTime;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [
        new NpgsqlParameter<long[]>("instrument_report_ids", _instrumentReportIds),
        new NpgsqlParameter<DateTime>("now", _nowUtc),
    ];
}
