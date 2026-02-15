using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal sealed class UpdateCommonServiceStateStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE service_state"
        + " SET is_paused = @is_paused"
        + " WHERE service_name = @service_name";

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
