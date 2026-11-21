using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal sealed class UpdateCommonServiceStateStmt : NonQueryDbStmtBase {
    // Upsert: the service may not have a row yet (one row per service, see R__009__FixServiceStateTable.sql).
    private const string sql = "INSERT INTO service_state (service_name, is_paused)"
        + " VALUES (@service_name, @is_paused)"
        + " ON CONFLICT (service_name) DO UPDATE SET is_paused = EXCLUDED.is_paused";

    private readonly bool _pauseNotResume;
    private readonly string _serviceName;

    public UpdateCommonServiceStateStmt(bool pauseNotResume, string serviceName)
        : base(sql, nameof(UpdateCommonServiceStateStmt)) {
        _pauseNotResume = pauseNotResume;
        _serviceName = serviceName;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<bool>("is_paused", _pauseNotResume),
            new NpgsqlParameter<string>("service_name", _serviceName)
        };
    }
}
