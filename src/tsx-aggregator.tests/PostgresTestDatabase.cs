using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// A throwaway PostgreSQL database for tests of the real SQL (code review R58: until now only the in-memory fake was
/// tested). Each test class that uses it as a class fixture gets a new, empty database, migrated with the production
/// migrations, and dropped afterwards. Existing databases are never touched.
/// </summary>
/// <remarks>
/// The server is the local dev one from appsettings.json (127.0.0.1:5433), or the connection string in the
/// TSX_TEST_POSTGRES environment variable. With no server, tests marked <see cref="PostgresFactAttribute"/> are
/// reported as skipped rather than silently passing.
/// </remarks>
public sealed class PostgresTestDatabase : IAsyncLifetime {
    private const string DefaultServer = "Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres";

    private static readonly Lazy<string?> UnavailableReason = new(CheckServer);

    private ServiceProvider? _services;

    public string DatabaseName { get; } = "tsx_unittest_" + Guid.NewGuid().ToString("N");

    public string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = DatabaseName }.ConnectionString;

    public IDbmService Dbm => _services?.GetRequiredService<IDbmService>() ?? throw new InvalidOperationException("Not initialized");

    /// <summary>Why the Postgres tests cannot run here, or null if they can.</summary>
    public static string? WhyUnavailable => UnavailableReason.Value;

    private static string ServerConnectionString {
        get {
            var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TSX_TEST_POSTGRES") ?? DefaultServer) {
                Database = "postgres",
                Timeout = 3,
                Pooling = false,
            };
            return builder.ConnectionString;
        }
    }

    private static string? CheckServer() {
        try {
            using var conn = new NpgsqlConnection(ServerConnectionString);
            conn.Open();
            return null;
        } catch (Exception ex) {
            return $"No test PostgreSQL server ({ex.GetType().Name}: {ex.Message}). Set TSX_TEST_POSTGRES to run these tests.";
        }
    }

    public async Task InitializeAsync() {
        await using (var conn = new NpgsqlConnection(ServerConnectionString)) {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", conn);
            _ = await create.ExecuteNonQueryAsync();
        }

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:tsx-scraper"] = ConnectionString,
            ["DatabaseSchema"] = "public",
            ["MaxRetries"] = "1",
            ["RetryDelayMilliseconds"] = "10",
            ["MaxConcurrentStatements"] = "5",
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddSingleton(config);
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _ = services.AddSingleton<PostgresExecutor>();
        _ = services.AddSingleton<DbMigrations>();
        _ = services.AddSingleton<IDbmService, DbmService>(); // runs the migrations
        _services = services.BuildServiceProvider();
        _ = _services.GetRequiredService<IDbmService>();
    }

    public async Task DisposeAsync() {
        if (_services is not null)
            await _services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(ServerConnectionString);
        await conn.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", conn);
        _ = await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Runs SQL directly against the test database, for setting up rows and checking results.</summary>
    public async Task<object?> Scalar(string sql, params (string Name, object Value)[] parameters) {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach ((string name, object value) in parameters)
            _ = cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }
}

/// <summary>A test that needs <see cref="PostgresTestDatabase"/>: skipped, with the reason, when there is no server.</summary>
public sealed class PostgresFactAttribute : FactAttribute {
    public PostgresFactAttribute() {
        if (PostgresTestDatabase.WhyUnavailable is string reason)
            Skip = reason;
    }
}

/// <summary>A theory that needs <see cref="PostgresTestDatabase"/>: skipped, with the reason, when there is no server.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute {
    public PostgresTheoryAttribute() {
        if (PostgresTestDatabase.WhyUnavailable is string reason)
            Skip = reason;
    }
}
