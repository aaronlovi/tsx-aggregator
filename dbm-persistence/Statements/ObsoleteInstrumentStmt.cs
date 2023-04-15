using System;
using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal class ObsoleteInstrumentStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE instruments SET obsoleted_date = @obsoleted_date"
        + " WHERE instrument_id = @instrument_id";

    private readonly long _instrumentId;
    private readonly DateTime _obsoletedDate;

    public ObsoleteInstrumentStmt(long instrumentId, DateTime obsoletedDate) : base(sql, nameof(ObsoleteInstrumentStmt)) {
        _instrumentId = instrumentId;
        _obsoletedDate = obsoletedDate;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentId),
            new NpgsqlParameter<DateTime>("obsoleted_date", _obsoletedDate)
        };
    }
}
