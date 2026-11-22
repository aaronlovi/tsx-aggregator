using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// How the statement base classes and <see cref="PostgresExecutor.ExecuteWithRetry(IPostgresStatement, CancellationToken, int?)"/>
/// handle errors, against real PostgreSQL (code review P7 / R45). A lost connection is retried, an error in the SQL is not,
/// and a cancellation stays a cancellation instead of becoming a failed result.
/// </summary>
/// <remarks>
/// The statements count their attempts in a sequence, because <c>nextval</c> is not rolled back when the connection dies.
/// "Lost connection" is the statement terminating its own backend, which is what a database restart looks like to the client.
/// </remarks>
public sealed class PostgresExecutorRetrySqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    /// <summary>Terminates its own connection on the first <paramref name="failingAttempts"/> attempts, then returns 1.</summary>
    private static string DropConnectionSql(string seq, int failingAttempts) =>
        $"SELECT CASE WHEN nextval('{seq}') <= {failingAttempts} THEN pg_terminate_backend(pg_backend_pid())::int ELSE 1 END";

    private static string DivideByZeroSql(string seq) => $"SELECT 1 / (nextval('{seq}') * 0)::int";

    private async Task<string> NewSequence() {
        string seq = "p7_attempts_" + Guid.NewGuid().ToString("N");
        _ = await db.Scalar($"CREATE SEQUENCE {seq}");
        return seq;
    }

    private async Task<long> Attempts(string seq) {
        object? isCalled = await db.Scalar($"SELECT is_called FROM {seq}");
        return isCalled is true ? (long)(await db.Scalar($"SELECT last_value FROM {seq}"))! : 0;
    }

    private PostgresExecutor Executor(int maxRetries) {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:tsx-scraper"] = db.ConnectionString,
            ["MaxRetries"] = maxRetries.ToString(),
            ["RetryDelayMilliseconds"] = "10",
            ["MaxConcurrentStatements"] = "5",
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddSingleton(config);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return new PostgresExecutor(services.BuildServiceProvider());
    }

    public static TheoryData<string> StatementKinds => new() { "query", "non-query", "batch" };

    private static IPostgresStatement Statement(string kind, string sql) => kind switch {
        "query" => new ScalarQueryStmt(sql),
        "non-query" => new SqlNonQueryStmt(sql),
        "batch" => new SqlBatchStmt(sql),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [PostgresTheory]
    [MemberData(nameof(StatementKinds))]
    public async Task LostConnectionDuringStatement_IsRetried(string kind) {
        string seq = await NewSequence();
        using PostgresExecutor exec = Executor(maxRetries: 3);

        DbStmtResult res = await exec.ExecuteWithRetry(Statement(kind, DropConnectionSql(seq, 1)), CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await Attempts(seq)).Should().Be(2);
    }

    [PostgresFact]
    public async Task QueryRetried_ReturnsOnlyTheSuccessfulAttemptsRows() {
        string seq = await NewSequence();
        using PostgresExecutor exec = Executor(maxRetries: 3);
        var stmt = new ScalarQueryStmt(DropConnectionSql(seq, 2));

        DbStmtResult res = await exec.ExecuteWithRetry(stmt, CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        res.NumRows.Should().Be(1);
        stmt.Values.Should().Equal(1);
    }

    [PostgresFact]
    public async Task LostConnectionOnEveryAttempt_FailsAfterMaxRetriesWithTheLastError() {
        string seq = await NewSequence();
        using PostgresExecutor exec = Executor(maxRetries: 3);

        DbStmtResult res = await exec.ExecuteWithRetry(new ScalarQueryStmt(DropConnectionSql(seq, 100)), CancellationToken.None);

        res.Success.Should().BeFalse();
        (await Attempts(seq)).Should().Be(3);
        res.ErrMsg.Should().Contain(nameof(ScalarQueryStmt)).And.Contain("3 attempts");
        res.Exception.Should().BeAssignableTo<NpgsqlException>();
    }

    [PostgresFact]
    public async Task UnlimitedRetries_KeepTryingUntilTheConnectionHolds() {
        // DbmService.GetIdRange64 runs ReserveIdRangeStmt with overrideMaxRetries 0 ("unlimited") and throws on failure.
        string seq = await NewSequence();
        using PostgresExecutor exec = Executor(maxRetries: 1);

        DbStmtResult res = await exec.ExecuteWithRetry(new ScalarQueryStmt(DropConnectionSql(seq, 4)), CancellationToken.None, overrideMaxRetries: 0);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await Attempts(seq)).Should().Be(5);
    }

    [PostgresTheory]
    [MemberData(nameof(StatementKinds))]
    public async Task ErrorInTheSql_IsNotRetriedAndKeepsTheServerError(string kind) {
        string seq = await NewSequence();
        using PostgresExecutor exec = Executor(maxRetries: 3);

        DbStmtResult res = await exec.ExecuteWithRetry(Statement(kind, DivideByZeroSql(seq)), CancellationToken.None);

        res.Success.Should().BeFalse();
        (await Attempts(seq)).Should().Be(1);
        res.ErrMsg.Should().Contain("division by zero");
        res.Exception.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
    }

    [PostgresTheory]
    [MemberData(nameof(StatementKinds))]
    public async Task CancelledDuringStatement_ThrowsInsteadOfFailing(string kind) {
        using PostgresExecutor exec = Executor(maxRetries: 3);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Func<Task> act = () => exec.ExecuteWithRetry(Statement(kind, "SELECT pg_sleep(30)::text"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [PostgresFact]
    public async Task CancelledBeforeTheStatement_Throws() {
        using PostgresExecutor exec = Executor(maxRetries: 3);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => exec.ExecuteWithRetry(new ScalarQueryStmt("SELECT 1"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [PostgresFact]
    public async Task CannotConnect_FailsWithTheConnectionErrorInsteadOfThrowing() {
        // A server error while connecting (here: no such database) is a failed result, like any other statement error.
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:tsx-scraper"] = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "tsx_unittest_missing" }.ConnectionString,
            ["MaxRetries"] = "3",
            ["RetryDelayMilliseconds"] = "10",
            ["MaxConcurrentStatements"] = "5",
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddSingleton(config);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var exec = new PostgresExecutor(services.BuildServiceProvider());

        DbStmtResult res = await exec.ExecuteWithRetry(new ScalarQueryStmt("SELECT 1"), CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Exception.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InvalidCatalogName);
    }

    [PostgresFact]
    public async Task MissingSettings_UseTheDefaults() {
        // Before: a missing key set the value to 0, so MaxConcurrentStatements 0 made every statement wait forever.
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:tsx-scraper"] = db.ConnectionString,
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddSingleton(config);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        using var exec = new PostgresExecutor(services.BuildServiceProvider());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        DbStmtResult res = await exec.ExecuteWithRetry(new ScalarQueryStmt("SELECT 1"), cts.Token);

        res.Success.Should().BeTrue(res.ErrMsg);
    }

    private sealed class ScalarQueryStmt(string sql) : QueryDbStmtBase(sql, nameof(ScalarQueryStmt)) {
        public List<int> Values { get; } = [];

        protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];

        protected override void ClearResults() => Values.Clear();

        protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
            Values.Add(reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)));
            return true;
        }
    }

    private sealed class SqlNonQueryStmt(string sql) : NonQueryDbStmtBase(sql, nameof(SqlNonQueryStmt)) {
        protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];
    }

    private sealed class SqlBatchStmt : NonQueryBatchedDbStmtBase {
        public SqlBatchStmt(string sql) : base(nameof(SqlBatchStmt)) => AddCommandToBatch(sql, []);
    }
}
