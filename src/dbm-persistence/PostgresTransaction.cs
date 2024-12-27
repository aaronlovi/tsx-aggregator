using System;
using Npgsql;

namespace dbm_persistence;

public sealed class PostgresTransaction : IDisposable {
    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }
    public SemaphoreLocker Limiter { get; }

    public PostgresTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction, SemaphoreLocker limiter) {
        Connection = connection;
        Transaction = transaction;
        Limiter = limiter;
    }

    public void Commit() => Transaction.Commit();

    public void Rollback() => Transaction.Rollback();

    public void Dispose() {
        Transaction.Dispose();
        Connection.Dispose();
        Limiter.Dispose();
    }
}
