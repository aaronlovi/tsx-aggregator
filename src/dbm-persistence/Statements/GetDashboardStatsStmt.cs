using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetDashboardStatsStmt : QueryDbStmtBase {
    private const string sql =
        "WITH active_instruments AS ("
        + " SELECT COUNT(*) AS cnt FROM instruments WHERE obsoleted_date IS NULL"
        + "), obsoleted_instruments AS ("
        + " SELECT COUNT(*) AS cnt FROM instruments WHERE obsoleted_date IS NOT NULL"
        + "), instruments_with_processed AS ("
        + " SELECT COUNT(DISTINCT instrument_id) AS cnt FROM processed_instrument_reports WHERE obsoleted_date IS NULL"
        + "), latest_raw AS ("
        + " SELECT MAX(created_date) AS dt FROM instrument_reports"
        + "), latest_processed AS ("
        + " SELECT MAX(created_date) AS dt FROM processed_instrument_reports"
        + "), unprocessed_events AS ("
        + " SELECT COUNT(*) AS cnt FROM instrument_events WHERE is_processed = false"
        + "), manual_review AS ("
        + " SELECT COUNT(*) AS cnt FROM instrument_reports WHERE check_manually = true AND is_current = true"
        + ")"
        + " SELECT"
        + " (SELECT cnt FROM active_instruments) AS total_active_instruments,"
        + " (SELECT cnt FROM obsoleted_instruments) AS total_obsoleted_instruments,"
        + " (SELECT cnt FROM instruments_with_processed) AS instruments_with_processed_reports,"
        + " (SELECT dt FROM latest_raw) AS most_recent_raw_ingestion,"
        + " (SELECT dt FROM latest_processed) AS most_recent_aggregation,"
        + " (SELECT cnt FROM unprocessed_events) AS unprocessed_event_count,"
        + " (SELECT cnt FROM manual_review) AS manual_review_count";

    private static int _totalActiveIndex = -1;
    private static int _totalObsoletedIndex = -1;
    private static int _withProcessedIndex = -1;
    private static int _latestRawIndex = -1;
    private static int _latestProcessedIndex = -1;
    private static int _unprocessedEventsIndex = -1;
    private static int _manualReviewIndex = -1;

    private long _totalActive;
    private long _totalObsoleted;
    private long _withProcessed;
    private DateTimeOffset? _latestRaw;
    private DateTimeOffset? _latestProcessed;
    private long _unprocessedEvents;
    private long _manualReview;

    public GetDashboardStatsStmt() : base(sql, nameof(GetDashboardStatsStmt)) { }

    public long TotalActiveInstruments => _totalActive;
    public long TotalObsoletedInstruments => _totalObsoleted;
    public long InstrumentsWithProcessedReports => _withProcessed;
    public DateTimeOffset? MostRecentRawIngestion => _latestRaw;
    public DateTimeOffset? MostRecentAggregation => _latestProcessed;
    public long UnprocessedEventCount => _unprocessedEvents;
    public long ManualReviewCount => _manualReview;

    protected override void ClearResults() {
        _totalActive = 0;
        _totalObsoleted = 0;
        _withProcessed = 0;
        _latestRaw = null;
        _latestProcessed = null;
        _unprocessedEvents = 0;
        _manualReview = 0;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        Array.Empty<NpgsqlParameter>();

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_totalActiveIndex != -1)
            return;

        _totalActiveIndex = reader.GetOrdinal("total_active_instruments");
        _totalObsoletedIndex = reader.GetOrdinal("total_obsoleted_instruments");
        _withProcessedIndex = reader.GetOrdinal("instruments_with_processed_reports");
        _latestRawIndex = reader.GetOrdinal("most_recent_raw_ingestion");
        _latestProcessedIndex = reader.GetOrdinal("most_recent_aggregation");
        _unprocessedEventsIndex = reader.GetOrdinal("unprocessed_event_count");
        _manualReviewIndex = reader.GetOrdinal("manual_review_count");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _totalActive = reader.GetInt64(_totalActiveIndex);
        _totalObsoleted = reader.GetInt64(_totalObsoletedIndex);
        _withProcessed = reader.GetInt64(_withProcessedIndex);
        _latestRaw = reader.IsDBNull(_latestRawIndex) ? null : reader.GetDateTime(_latestRawIndex);
        _latestProcessed = reader.IsDBNull(_latestProcessedIndex) ? null : reader.GetDateTime(_latestProcessedIndex);
        _unprocessedEvents = reader.GetInt64(_unprocessedEventsIndex);
        _manualReview = reader.GetInt64(_manualReviewIndex);
        return false; // Single-row result
    }
}
