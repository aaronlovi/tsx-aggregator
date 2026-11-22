using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace dbm_persistence;

public interface IPostgresStatement {
    Task<DbStmtResult> Execute(NpgsqlConnection conn, CancellationToken ct);
}

public class PostgresExecutor : IDisposable {
    private readonly ILogger<PostgresExecutor> _logger;
    private int _maxRetries = 5; // 0 = unlimited
    private int _retryDelayMilliseconds = 1000; // 0 = no delay
    private int _maxConcurrentStatements = 20;
    private readonly string _connectionString;

    private readonly SemaphoreSlim _connectionLimiter;
    private bool _disposedValue;

    public PostgresExecutor(IServiceProvider svp) {
        IConfiguration config = svp.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("tsx-scraper") ?? string.Empty;
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("Connection string is empty");

        _logger = svp.GetRequiredService<ILogger<PostgresExecutor>>();
        _logger.LogInformation("Persistence is enabled");

        Parse("MaxRetries", ref _maxRetries);
        Parse("RetryDelayMilliseconds", ref _retryDelayMilliseconds);
        Parse("MaxConcurrentStatements", ref _maxConcurrentStatements);

        _logger.LogInformation("DatabaseManager settings: (maxRetries:{MaxRetries}, retryDelayMilliseconds: {RetryDelayMilliseconds}, maxConcurrentStatements: {MaxConcurrentStatements})",
            _maxRetries, _retryDelayMilliseconds, _maxConcurrentStatements);

        _connectionLimiter = new(_maxConcurrentStatements);

        // Local helper methods

        // A missing or unreadable setting keeps its default (it used to become 0: no retry limit, no delay, no connections).
        void Parse(string paramName, ref int paramValue) {
            if (int.TryParse(config[paramName], out int parsed) && parsed >= 0)
                paramValue = parsed;
            else
                _logger.LogWarning("Failed to parse {ParamName}, using default value {ParamValue}", paramName, paramValue);
        }
    }

    public bool IsEnabled => !string.IsNullOrEmpty(_connectionString);

    /// <summary>
    /// Runs the statement once on a new connection. A database error, while connecting or in the statement, is a failed
    /// result that carries the exception; only a cancellation of <paramref name="ct"/> is thrown.
    /// </summary>
    public async Task<DbStmtResult> Execute(IPostgresStatement stmt, CancellationToken ct) {
        using var limiter = new SemaphoreLocker(_connectionLimiter);
        await limiter.Acquire(ct);

        try {
            await using var connection = new NpgsqlConnection(_connectionString); // Does not throw
            await connection.OpenAsync(ct);
            return await stmt.Execute(connection, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (NpgsqlException ex) {
            return DbStmtResult.StatementFailure($"{stmt.GetType().Name}: connecting to the database failed - {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs the statement, trying again after a lost connection or another transient error (code review P7 / R45).
    /// An error in the SQL or the data is not retried. Gives up after the configured number of attempts
    /// (<paramref name="overrideMaxRetries"/> if given; 0 means no limit) and returns the last failure.
    /// </summary>
    public async Task<DbStmtResult> ExecuteWithRetry(IPostgresStatement stmt, CancellationToken ct, int? overrideMaxRetries = null) {
        int maxAttempts = overrideMaxRetries ?? _maxRetries;
        if (maxAttempts <= 0)
            maxAttempts = int.MaxValue;

        for (int attempt = 1; ; attempt++) {
            DbStmtResult res = await Execute(stmt, ct);
            if (res.Success)
                return res;

            bool retryable = IsRetryable(res.Exception);
            if (!retryable || attempt >= maxAttempts) {
                _logger.LogError(res.Exception, "{Statement} failed after {Attempts} attempt(s): {Error}", stmt.GetType().Name, attempt, res.ErrMsg);
                return attempt == 1 ? res : res with { ErrMsg = $"{res.ErrMsg} (gave up after {attempt} attempts)" };
            }

            _logger.LogWarning(res.Exception, "{Statement} attempt {Attempt} failed, retrying in {Delay} ms: {Error}",
                stmt.GetType().Name, attempt, _retryDelayMilliseconds, res.ErrMsg);
            await Task.Delay(TimeSpan.FromMilliseconds(_retryDelayMilliseconds), ct);
        }
    }

    /// <summary>
    /// A lost or refused connection, or a server error that says to try again (shutdown, restart, serialization failure,
    /// deadlock, out of resources). Each statement runs alone on its own connection, so it is safe to run again.
    /// </summary>
    internal static bool IsRetryable(Exception? ex) => ex switch {
        PostgresException pg => pg.IsTransient || pg.SqlState is PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown or PostgresErrorCodes.CannotConnectNow,
        NpgsqlException npg => npg.IsTransient,
        _ => false,
    };

    #region IDisposable implementation

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing)
                _connectionLimiter.Dispose();

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Pub cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

public sealed class SemaphoreLocker : IDisposable {
    private readonly SemaphoreSlim _semaphore;
    private bool _acquired;

    public SemaphoreLocker(SemaphoreSlim semaphore) => _semaphore = semaphore;

    public async Task Acquire(CancellationToken ct) {
        await _semaphore.WaitAsync(ct);
        _acquired = true;
    }

    public void Dispose() {
        if (_acquired) {
            _ = _semaphore.Release();
            _acquired = false;
        }
    }
}
