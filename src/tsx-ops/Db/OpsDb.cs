using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace tsx_ops.Db;

/// <summary>
/// Database access for ops commands. Read-only commands run all their queries in one REPEATABLE READ READ ONLY
/// transaction: a consistent point-in-time view while production keeps writing, and Postgres rejects any write.
/// Never runs migrations.
/// </summary>
internal static class OpsDb {
    public static async Task<T> RunReadOnlyAsync<T>(
        string connectionString,
        int statementTimeoutSeconds,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        CancellationToken ct) {

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        // Must be the first statements in the transaction, before any query takes its snapshot.
        string setup = "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = "
            + (statementTimeoutSeconds * 1000).ToString(CultureInfo.InvariantCulture);
        await using (var cmd = new NpgsqlCommand(setup, conn, tx))
            _ = await cmd.ExecuteNonQueryAsync(ct);

        T result = await work(conn, tx, ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
