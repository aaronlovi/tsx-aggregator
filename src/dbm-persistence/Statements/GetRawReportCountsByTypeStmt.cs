using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetRawReportCountsByTypeStmt : QueryDbStmtBase {
    private const string sql =
        "SELECT report_type, COUNT(*) AS cnt"
        + " FROM instrument_reports"
        + " WHERE is_current = true AND ignore_report = false"
        + " GROUP BY report_type"
        + " ORDER BY report_type";

    private static int _reportTypeIndex = -1;
    private static int _countIndex = -1;

    private readonly List<RawReportCountByTypeDto> _counts;

    public GetRawReportCountsByTypeStmt() : base(sql, nameof(GetRawReportCountsByTypeStmt)) {
        _counts = [];
    }

    public IReadOnlyList<RawReportCountByTypeDto> Counts => _counts;

    protected override void ClearResults() => _counts.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        [];

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_reportTypeIndex != -1)
            return;

        _reportTypeIndex = reader.GetOrdinal("report_type");
        _countIndex = reader.GetOrdinal("cnt");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        int reportType = reader.GetInt32(_reportTypeIndex);
        long count = reader.GetInt64(_countIndex);
        _counts.Add(new RawReportCountByTypeDto(reportType, count));
        return true; // Continue processing all rows
    }
}
