using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace dbm_persistence;

internal static class DbUtils {
    static internal NpgsqlParameter CreateNullableDateTimeParam(string paramName, DateTimeOffset? nullableDate) {
        var param = new NpgsqlParameter(paramName, NpgsqlDbType.TimestampTz) {
            Value = nullableDate?.UtcDateTime ?? (object)DBNull.Value
        };
        return param;
    }
}

internal abstract class DbStmtBase {
    protected abstract IReadOnlyCollection<NpgsqlParameter> GetBoundParameters();
}

internal abstract class QueryDbStmtBase : DbStmtBase, IPostgresStatement {
    private readonly string _sql;
    private readonly string _className;

    protected QueryDbStmtBase(string sql, string className) {
        _sql = sql;
        _className = className;
    }

    public async Task<DbStmtResult> Execute(NpgsqlConnection conn, CancellationToken ct) {
        ClearResults();

        try {
            using var cmd = new NpgsqlCommand(_sql, conn);
            foreach (NpgsqlParameter boundParam in GetBoundParameters())
                cmd.Parameters.Add(boundParam);
            await cmd.PrepareAsync(ct);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            int numRows = 0;
            while (await reader.ReadAsync(ct)) {
                ++numRows;
                if (!ProcessCurrentRow(reader))
                    break;
            }

            return DbStmtResult.StatementSuccess(numRows);
        }
        catch (Exception ex) {
            ClearResults();
            string errMsg = $"{_className} failed - {ex.Message}";
            return DbStmtResult.StatementFailure(errMsg);
        }
    }

    protected abstract void ClearResults();
    protected abstract bool ProcessCurrentRow(NpgsqlDataReader reader);
}

internal abstract class NonQueryDbStmtBase : DbStmtBase, IPostgresStatement {
    private readonly string _sql;
    private readonly string _className;

    protected NonQueryDbStmtBase(string sql, string className) {
        _sql = sql;
        _className = className;
    }

    public async Task<DbStmtResult> Execute(NpgsqlConnection conn, CancellationToken ct) {
        try {
            using var cmd = new NpgsqlCommand(_sql, conn);
            foreach (NpgsqlParameter boundParam in GetBoundParameters())
                _ = cmd.Parameters.Add(boundParam);
            await cmd.PrepareAsync(ct);
            int numRows = await cmd.ExecuteNonQueryAsync(ct);
            return DbStmtResult.StatementSuccess(numRows);
        }
        catch (Exception ex) {
            string errMsg = $"{_className} failed - {ex.Message}";
            return DbStmtResult.StatementFailure(errMsg);
        }
    }
}

internal abstract class NonQueryBatchedDbStmtBase : IPostgresStatement {
    private readonly string _className;
    private readonly List<NpgsqlBatchCommand> _commands;

    protected NonQueryBatchedDbStmtBase(string className) {
        _className = className;
        _commands = new();
    }

    public async Task<DbStmtResult> Execute(NpgsqlConnection conn, CancellationToken ct) {
        try {
            using var batch = new NpgsqlBatch(conn);
            foreach (NpgsqlBatchCommand cmd in _commands)
                batch.BatchCommands.Add(cmd);
            int numRows = await batch.ExecuteNonQueryAsync(ct);
            return DbStmtResult.StatementSuccess(numRows);
        }
        catch (Exception ex) {
            string errMsg = $"{_className} failed - {ex.Message}";
            return DbStmtResult.StatementFailure(errMsg);
        }
    }

    protected void AddCommandToBatch(string sql, IReadOnlyCollection<NpgsqlParameter> boundParams) {
        var cmd = new NpgsqlBatchCommand(sql);
        foreach (NpgsqlParameter boundParam in boundParams)
            cmd.Parameters.Add(boundParam);
        _commands.Add(cmd);
    }
}
