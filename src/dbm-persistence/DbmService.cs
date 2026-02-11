using System;
using System.Collections.Generic;
using System.Text.Json;
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

    public async ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct) {
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

    public async ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct) {
        var stmt = new GetInstrumentListStmt();
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.Instruments);
    }

    public async ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct) {
        var stmt = new UpdateInstrumentListStmt(newInstrumentList, obsoletedInstrumentList);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<(Result, ApplicationCommonState?)> GetApplicationCommonState(CancellationToken ct) {
        var stmt = new GetApplicationCommonStateStmt();
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (!res.Success)
            return (res, null);
        if (res.Success && res.NumRows == 0)
            return (Result.SetFailure("No state found"), null);
        return (res, stmt.Results);
    }

    public async ValueTask<Result> PersistStateFsmState(ApplicationCommonState stateFsmState, CancellationToken ct) {
        var stmt = new UpdateStateFsmStateStmt(stateFsmState);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct) {
        var stmt = new UpdateNextTimeToFetchQuotesStmt(nextTimeToFetchQuotes);
        return await _exec.ExecuteWithRetry(stmt, ct);
    }

    public async ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct) {
        var stmt = new GetRawFinancialsByInstrumentIdStmt(instrumentId);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.InstrumentReports);
    }

    public async ValueTask<Result> UpdateRawInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct) {
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

    public async ValueTask<Result<PagedInstrumentsWithRawDataReportUpdatesDto>> GetRawInstrumentsWithUpdatedDataReports(
        string exchange, int pageNumber, int pageSize, CancellationToken ct) {
        var stmt = new GetRawInstrumentsWithUpdatedDataReportsStmt(exchange, pageNumber, pageSize);
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (res.Success) {
            _logger.LogInformation("GetRawInstrumentsWithUpdatedDataReports success - Page: {PageNumber}, Size: {PageSize}, Total: {Total}",
                pageNumber, pageSize, stmt.PagedInstrumentsWithRawDataReportUpdates.TotalInstruments);
        }
        else {
            _logger.LogWarning("GetRawInstrumentsWithUpdatedDataReports failed with error {Error}", res.ErrMsg);
        }
        return new Result<PagedInstrumentsWithRawDataReportUpdatesDto>(
            res.Success, res.ErrMsg, stmt.PagedInstrumentsWithRawDataReportUpdates);
    }

    public async ValueTask<Result<PagedInstrumentInfoDto>> GetInstrumentsWithNoRawReports(
        string exchange, int pageNumber, int pageSize, CancellationToken ct) {
        var stmt = new GetInstrumentsWithNoRawReportsStmt(exchange, pageNumber, pageSize);
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (res.Success) {
            _logger.LogInformation("GetInstrumentsWithNoRawReports success - Page: {PageNumber}, Size: {PageSize}, Total: {Total}",
                pageNumber, pageSize, stmt.PagedInstrumentInfo.TotalInstruments);
        } else {
            _logger.LogWarning("GetInstrumentsWithNoRawReports failed with error {Error}", res.ErrMsg);
        }
        return new Result<PagedInstrumentInfoDto>(res.Success, res.ErrMsg, stmt.PagedInstrumentInfo);
    }

    public async ValueTask<Result> IgnoreRawUpdatedDataReport(RawInstrumentReportsToKeepAndIgnoreDto dto, CancellationToken ct) {
        var getInstrumentReportsStmt = new GetInstrumentReportsStmt(dto.InstrumentId);
        var res = await _exec.ExecuteWithRetry(getInstrumentReportsStmt, ct);
        if (!res.Success) {
            _logger.LogWarning("IgnoreRawUpdatedDataReport failed while getting raw instrument reports with error {Error}", res.ErrMsg);
            return res;
        }

        var consistencyMap = new RawReportConsistencyMap();
        RawReportConsistencyMapKey? mainKey = consistencyMap.BuildMap(dto, getInstrumentReportsStmt.InstrumentReports);

        if (mainKey is null) {
            _logger.LogWarning("IgnoreRawUpdatedDataReport failed - report to keep not found");
            return Result.SetFailure("Report to keep not found");
        }

        Result result = consistencyMap.EnsureRequestIsConsistent(dto, mainKey);
        if (!result.Success) {
            _logger.LogWarning("IgnoreRawUpdatedDataReport failed - consistency check failed with error {Error}", result.ErrMsg);
            return DbStmtResult.StatementFailure(result.ErrMsg);
        }

        // If we got here, then the request is valid, so ignore the reports
        var stmt = new IgnoreRawDataReportStmt(dto.InstrumentId, dto.ReportIdToKeep, dto.ReportIdsToIgnore);
        res = await _exec.ExecuteWithRetry(stmt, ct);
        if (res.Success) {
            _logger.LogInformation("IgnoreRawUpdatedDataReport - ignore statement success");
        } else {
            _logger.LogWarning("IgnoreRawUpdatedDataReport - ignore statement failed with error {Error}", res.ErrMsg);
        }

        return res;
    }

    public ValueTask<Result> UpsertRawCurrentInstrumentReport(CurrentInstrumentRawDataReportDto rawReportData, CancellationToken ct)
        => throw new NotImplementedException();

    public async ValueTask<(Result res, bool existsMatching)> ExistsMatchingRawReport(
        CurrentInstrumentRawDataReportDto rawReportDto, CancellationToken ct) {
        var getInstrumentReportsStmt = new GetInstrumentReportsStmt(rawReportDto.InstrumentId);

        var res = await _exec.ExecuteWithRetry(getInstrumentReportsStmt, ct);
        if (!res.Success) {
            _logger.LogWarning("ExistsMatchingRawReport failed while getting raw instrument reports with error {Error}", res.ErrMsg);
            return Failure("Failed getting raw instrument reports: " + res.ErrMsg);
        }

        var newReportData = RawReportDataMap.FromJsonString(rawReportDto.ReportJson);

        foreach (var existingReport in getInstrumentReportsStmt.InstrumentReports) {
            if (existingReport.ObsoletedDate is not null)
                continue;
            if (existingReport.ReportType != rawReportDto.ReportType)
                continue;
            if (existingReport.ReportPeriodType != rawReportDto.ReportPeriodType)
                continue;
            if (existingReport.ReportDate != rawReportDto.ReportDate)
                continue;

            using JsonDocument existingReportData = JsonDocument.Parse(existingReport.ReportJson);

            if (newReportData.IsEqual(existingReportData))
                return FoundMatch();
        }

        return FoundNoMatch();

        // Local helper methods

        (Result res, bool existsMatching) Failure(string errMsg) => (new Result(false, errMsg), false);

        (Result res, bool existsMatching) FoundMatch() => (Result.SUCCESS, true);
        
        (Result res, bool existsMatching) FoundNoMatch() => (Result.SUCCESS, false);
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

    #region Dashboard

    public async ValueTask<(Result, DashboardStatsDto?)> GetDashboardStats(CancellationToken ct) {
        var statsStmt = new GetDashboardStatsStmt();
        DbStmtResult statsRes = await _exec.ExecuteWithRetry(statsStmt, ct);
        if (!statsRes.Success)
            return (statsRes, null);

        var countsStmt = new GetRawReportCountsByTypeStmt();
        DbStmtResult countsRes = await _exec.ExecuteWithRetry(countsStmt, ct);
        if (!countsRes.Success)
            return (countsRes, null);

        var dto = new DashboardStatsDto(
            TotalActiveInstruments: statsStmt.TotalActiveInstruments,
            TotalObsoletedInstruments: statsStmt.TotalObsoletedInstruments,
            InstrumentsWithProcessedReports: statsStmt.InstrumentsWithProcessedReports,
            MostRecentRawIngestion: statsStmt.MostRecentRawIngestion,
            MostRecentAggregation: statsStmt.MostRecentAggregation,
            UnprocessedEventCount: statsStmt.UnprocessedEventCount,
            ManualReviewCount: statsStmt.ManualReviewCount,
            RawReportCountsByType: countsStmt.Counts);

        return (Result.SUCCESS, dto);
    }

    #endregion

    #region Service State

    public async ValueTask<(Result, bool)> GetCommonServiceState(string serviceName, CancellationToken ct) {
        var stmt = new GetCommonServiceStateStmt(serviceName);
        DbStmtResult res = await _exec.ExecuteWithRetry(stmt, ct);
        return (res, stmt.IsPaused);
    }

    public async ValueTask<Result> PersistCommonServiceState(bool isPaused, string serviceName, CancellationToken ct) {
        var stmt = new UpdateCommonServiceStateStmt(isPaused, serviceName);
        var res = await _exec.ExecuteWithRetry(stmt, ct);
        if (res.Success) {
            _logger.LogInformation("PersistCommonServiceState success - SVC: {ServiceName}, PAUSED: {IsPaused}",
                serviceName, isPaused);
        } else {
            _logger.LogWarning("PersistCommonServiceState failed with error {Error}", res.ErrMsg);
        }
        return res;
    }

    #endregion

    public void Dispose() {
        // Dispose of Postgres executor
        _exec.Dispose();

        // Dispose of the generator mutex
        _generatorMutex.Dispose();
    }
}
