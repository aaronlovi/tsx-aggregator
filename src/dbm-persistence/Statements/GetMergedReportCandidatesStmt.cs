using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

/// <summary>
/// Current raw reports of listed instruments that share a calendar year (annual) or quarter (others) with another current
/// report of their type, or whose JSON has another report date than their row (review R31, tsx-ops merged-reports).
/// </summary>
internal sealed class GetMergedReportCandidatesStmt : QueryDbStmtBase {
    private const string sql = "SELECT r.instrument_report_id, r.instrument_id, i.company_symbol, i.instrument_symbol,"
        + " r.report_type, r.report_period_type, r.report_date, r.report_json,"
        + " EXISTS (SELECT 1 FROM instrument_reports h"
        + "   WHERE h.instrument_id = r.instrument_id AND h.report_type = r.report_type"
        + "   AND h.report_period_type = r.report_period_type AND h.report_date = r.report_date"
        + "   AND h.is_current = FALSE) AS already_reset"
        + " FROM instrument_reports r"
        + " JOIN instruments i ON i.instrument_id = r.instrument_id AND i.obsoleted_date IS NULL"
        + " WHERE r.is_current = TRUE"
        + " AND (substring(r.report_json from '\"REPORTDATE\":\\s*\"(\\d{4}-\\d{2}-\\d{2})')"
        + "     <> to_char(r.report_date AT TIME ZONE 'UTC', 'YYYY-MM-DD')"
        + "   OR EXISTS (SELECT 1 FROM instrument_reports o"
        + "     WHERE o.instrument_id = r.instrument_id AND o.report_type = r.report_type"
        + "     AND o.report_period_type = r.report_period_type AND o.report_date <> r.report_date AND o.is_current = TRUE"
        + "     AND date_trunc(CASE WHEN r.report_period_type = 1 THEN 'year' ELSE 'quarter' END, o.report_date AT TIME ZONE 'UTC')"
        + "       = date_trunc(CASE WHEN r.report_period_type = 1 THEN 'year' ELSE 'quarter' END, r.report_date AT TIME ZONE 'UTC')))";

    private readonly List<MergedReportCandidateDto> _candidates = [];

    public GetMergedReportCandidatesStmt() : base(sql, nameof(GetMergedReportCandidatesStmt)) { }

    public IReadOnlyList<MergedReportCandidateDto> Candidates => _candidates;

    protected override void ClearResults() => _candidates.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _candidates.Add(new MergedReportCandidateDto(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            DateOnly.FromDateTime(reader.GetDateTime(6)),
            reader.GetString(7),
            reader.GetBoolean(8)));
        return true;
    }
}
