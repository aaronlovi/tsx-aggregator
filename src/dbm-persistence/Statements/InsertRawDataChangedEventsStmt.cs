using System;
using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

/// <summary>One unprocessed RawDataChanged (4) event per instrument, in one statement (O1, decision 26).</summary>
internal sealed class InsertRawDataChangedEventsStmt : NonQueryDbStmtBase {
    private const string sql = "INSERT INTO instrument_events (instrument_id, event_date, event_type, is_processed)"
        + " SELECT id, @event_date, 4, FALSE FROM unnest(@instrument_ids) AS id";

    private readonly long[] _instrumentIds;
    private readonly DateTime _eventDateUtc;

    public InsertRawDataChangedEventsStmt(IReadOnlyCollection<long> instrumentIds, DateTimeOffset eventDate)
        : base(sql, nameof(InsertRawDataChangedEventsStmt)) {
        _instrumentIds = [.. instrumentIds];
        _eventDateUtc = eventDate.UtcDateTime;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [
        new NpgsqlParameter<long[]>("instrument_ids", _instrumentIds),
        new NpgsqlParameter<DateTime>("event_date", _eventDateUtc),
    ];
}
