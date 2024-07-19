using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

public sealed class DbmService : IDisposable, IDbmService {

    private readonly ILogger<DbmService> _logger;
    private readonly PostgresExecutor _exec;
    private readonly SemaphoreSlim _generatorMutex;

    private ulong _lastUsed;
    private ulong _endId;

    public DbmService(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<DbmService>>();
        _exec = svp.GetRequiredService<PostgresExecutor>();

        IConfiguration config = svp.GetRequiredService<IConfiguration>();
        string connStr = config.GetConnectionString("tsx-scraper") ?? string.Empty;
        if (string.IsNullOrEmpty(connStr))
            throw new InvalidOperationException("Connection string is empty");

        _generatorMutex = new(1);

        // Perform the DB migrations synchronously
        try {
            DbMigrations migrations = svp.GetRequiredService<DbMigrations>();
            migrations.Up();
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to perform DB migrations, aborting");
            throw;
        }
    }

    #region Generator

    public ValueTask<ulong> GetNextId64(CancellationToken ct) => GetIdRange64(1, ct);

    public async ValueTask<ulong> GetIdRange64(uint count, CancellationToken ct) {
        if (count == 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be 0");

        // Optimistic path: in most cases
        lock (_generatorMutex) {
            if (_lastUsed + count <= _endId) {
                var result = _lastUsed + 1;
                _lastUsed += count;
                return result;
            }
        }

        // Lock the DB update mutex
        using var locker = new SemaphoreLocker(_generatorMutex);
        await locker.Acquire(ct);

        // May have bene changed already by another thread, so check again
        lock (_generatorMutex) {
            if (_lastUsed + count <= _endId) {
                var result = _lastUsed + 1;
                _lastUsed += count;
                return result;
            }
        }

        // Update in blocks
        const uint BLOCK_SIZE = 65536;
        uint idRange = count - (count % BLOCK_SIZE) + BLOCK_SIZE;
        var stmt = new ReserveIdRangeStmt(idRange);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct, 0);

        if (res.Success) {
            lock (_generatorMutex) {
                _endId = (ulong)stmt.LastReserved;
                _lastUsed = (ulong)(stmt.LastReserved - BLOCK_SIZE);
                var result = _lastUsed + 1;
                _lastUsed += count;
                return result;
            }
        } else {
            throw new InvalidOperationException("Failed to get next id from database");
        }
    }

    #endregion

    #region Aggregated

    public async ValueTask<(Result, InstrumentEventExDto?)> GetNextInstrumentEvent(CancellationToken ct) {
        var stmt = new GetNextInstrumentEventStmt();
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.InstrumentEventDto);
    }

    public async ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct) {
        var stmt = new GetCurrentInstrumentReportsStmt(instrumentId);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.InstrumentReports);
    }

    public async ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventExDto dto, CancellationToken ct) {
        var stmt = new UpdateInstrumentEventStmt(dto);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct) {
        var stmt = new InsertProcessedCompanyReportStmt(dto);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    #endregion

    #region Raw

    public async ValueTask<Result> InsertInstrumentIfNotExists(
        string companySymbol,
        string companyName,
        string instrumentSymbol,
        string instrumentName,
        string exchange,
        CancellationToken ct) {
        var stmt = new GetInstrumentBySymbolAndExchangeStmt(companySymbol, instrumentSymbol, exchange);
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (!res.Success || res.NumRows == 0) {
            var instrumentId = (long)await GetNextId64(ct);
            var instrumentDto = new InstrumentDto(instrumentId, exchange, companySymbol, companyName, instrumentSymbol, instrumentName, DateTime.UtcNow, null);
            var insertInstrumentStmt = new InsertInstrumentStmt(instrumentDto);
            res = await _exec.ExecuteWithRetry(insertInstrumentStmt, ct);
        }

        return res;
    }

    public async ValueTask<Result> ObsoleteInstrument(
        string companySymbol,
        string instrumentSymbol,
        string exchange,
        CancellationToken ct) {
        var getInstrumentStmt = new GetInstrumentBySymbolAndExchangeStmt(companySymbol, instrumentSymbol, exchange);
        var res = await _exec.ExecuteWithRetry(getInstrumentStmt, ct);
        if (res.Success && res.NumRows > 0) {
            var obsoleteInstrumentStmt = new ObsoleteInstrumentStmt(getInstrumentStmt.Results!.InstrumentId, DateTime.UtcNow);
            res = await _exec.ExecuteWithRetry(obsoleteInstrumentStmt, ct);
        }

        return res;
    }

    public async ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct) {
        var stmt = new GetInstrumentListStmt();
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.Instruments);
    }

    public async ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct) {
        var stmt = new UpdateInstrumentListStmt(newInstrumentList, obsoletedInstrumentList);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<Result> InsertInstrumentEvent(InstrumentEventExDto instrumentEventDto, CancellationToken ct) {
        var stmt = new InsertInstrumentEventStmt(instrumentEventDto);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<(Result, StateFsmState?)> GetStateFsmState(CancellationToken ct) {
        var stmt = new GetStateFsmStateStmt();
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (!res.Success)
            return (res, null);
        if (res.Success && res.NumRows == 0)
            return (Result.SetFailure("No state found"), null);
        return (res, stmt.Results);
    }

    public async ValueTask<Result> PersistStateFsmState(StateFsmState stateFsmState, CancellationToken ct) {
        var stmt = new UpdateStateFsmStateStmt(stateFsmState);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct) {
        var stmt = new UpdateNextTimeToFetchQuotesStmt(nextTimeToFetchQuotes);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct) {
        var stmt = new GetRawFinancialsByInstrumentIdStmt(instrumentId);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.InstrumentReports);
    }

    public async ValueTask<Result> UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct) {
        var stmt = new UpdateInstrumentReportsStmt(rawFinancialsDelta);
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (res.Success) {
            _logger.LogInformation("UpdateInstrumentReports success - Inserted: {NumInserted}, Obsoleted: {NumObsoleted}, To Check Manually: {NumToCheckManually}",
                stmt.NumReportsToInsert, stmt.NumReportsToObsolete, stmt.NumReportsToCheckManually);
        } else {
            _logger.LogWarning("UpdateInstrumentReports failed with error {Error}", res.ErrMsg);
        }
        return res;
    }

    #endregion

    #region Data Request

    public async ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct) {
        var stmt = new GetProcessedStockDataByExchangeStmt(exchange);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return new Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>(res.Success, res.ErrMsg, stmt.ProcessedInstrumentReports);
    }

    public async ValueTask<Result<ProcessedFullInstrumentReportDto>> GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol, CancellationToken ct) {
        var stmt = new GetProcessedStockDataByExchangeAndInstrumentSymbolStmt(exchange, instrumentSymbol);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return new Result<ProcessedFullInstrumentReportDto>(res.Success, res.ErrMsg, stmt.ProcessedInstrumentReport);
    }

    #endregion

    public void Dispose() {
        // Dispose of Postgres executor
        _exec.Dispose();

        // Dispose of the generator mutex
        _generatorMutex.Dispose();
    }
}
