using System;
using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal sealed class UpdateInstrumentLastScrapedDateStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE instruments SET last_scraped_date = @last_scraped_date WHERE instrument_id = @instrument_id";

    private readonly long _instrumentId;
    private readonly DateTime _lastScrapedDate;

    public UpdateInstrumentLastScrapedDateStmt(long instrumentId, DateTime lastScrapedDate)
        : base(sql, nameof(UpdateInstrumentLastScrapedDateStmt)) {
        _instrumentId = instrumentId;
        _lastScrapedDate = lastScrapedDate;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentId),
            new NpgsqlParameter<DateTime>("last_scraped_date", _lastScrapedDate)
        };
    }
}
