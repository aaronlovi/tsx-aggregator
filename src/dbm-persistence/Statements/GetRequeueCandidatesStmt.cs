using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

/// <summary>
/// Every listed instrument, with whether it has current raw reports and whether a RawDataChanged event is already
/// waiting for it (O1, decision 26).
/// </summary>
internal sealed class GetRequeueCandidatesStmt : QueryDbStmtBase {
    private const string sql = "SELECT i.instrument_id, i.instrument_symbol,"
        + " EXISTS (SELECT 1 FROM instrument_reports r"
        + "   WHERE r.instrument_id = i.instrument_id AND r.obsoleted_date IS NULL) AS has_raw_reports,"
        + " EXISTS (SELECT 1 FROM instrument_events e"
        + "   WHERE e.instrument_id = i.instrument_id AND e.event_type = 4 AND e.is_processed = FALSE) AS has_pending"
        + " FROM instruments i"
        + " WHERE i.obsoleted_date IS NULL";

    private readonly List<RequeueCandidateDto> _candidates;

    private static int _instrumentIdIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _hasRawReportsIndex = -1;
    private static int _hasPendingIndex = -1;

    public GetRequeueCandidatesStmt() : base(sql, nameof(GetRequeueCandidatesStmt)) => _candidates = [];

    public IReadOnlyList<RequeueCandidateDto> Candidates => _candidates;

    protected override void ClearResults() => _candidates.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentIdIndex != -1)
            return;

        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
        _hasRawReportsIndex = reader.GetOrdinal("has_raw_reports");
        _hasPendingIndex = reader.GetOrdinal("has_pending");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _candidates.Add(new RequeueCandidateDto(
            reader.GetInt64(_instrumentIdIndex),
            reader.GetString(_instrumentSymbolIndex),
            reader.GetBoolean(_hasRawReportsIndex),
            reader.GetBoolean(_hasPendingIndex)));
        return true;
    }
}
