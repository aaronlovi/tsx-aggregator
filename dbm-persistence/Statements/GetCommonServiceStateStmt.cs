using System.Collections.Generic;
using Npgsql;

namespace dbm_persistence;

internal sealed class GetCommonServiceStateStmt : QueryDbStmtBase {
    private const string sql = "SELECT is_paused"
        + " FROM service_state"
        + " WHERE service_name = @service_name";

    // Inputs
    private readonly string _serviceName;

    private static int _isPausedIndex = -1;

    // Results
    private bool _isPaused;

    public GetCommonServiceStateStmt(string serviceName) : base(sql, nameof(GetCommonServiceStateStmt)) {
        _serviceName = serviceName;
        _isPaused = false;
    }

    public bool IsPaused => _isPaused;

    protected override void ClearResults() => _isPaused = false;
    
    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        new NpgsqlParameter[] { new NpgsqlParameter<string>("service_name", _serviceName) };

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_isPausedIndex != -1)
            return;

        _isPausedIndex = reader.GetOrdinal("is_paused");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _isPaused = reader.GetBoolean(_isPausedIndex);
        return true;
    }
}
