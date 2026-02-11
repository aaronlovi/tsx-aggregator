using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

public sealed class DbmInMemory : IDbmService {
    private readonly ILogger<DbmInMemory> _logger;

    // Generator
    private ulong _lastUsed;
    private readonly object _lock;

    private readonly DbmInMemoryData _data;

    public DbmInMemory(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<DbmInMemory>>();
        _lock = new();
        _data = new();
        _logger.LogWarning("DbmInMemory is instantiated: persistence in RAM only");
    }

    #region GENERATOR

    public ValueTask<ulong> GetNextId64(CancellationToken ct) => GetIdRange64(1, ct);

    public ValueTask<ulong> GetIdRange64(uint count, CancellationToken ct) {
        if (count == 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be 0");

        // Always in memory
        lock (_lock) {
            var result = _lastUsed + 1;
            _lastUsed += count;
            return ValueTask.FromResult(result);
        }
    }

    #endregion

    #region AGGREGATED

    public ValueTask<(Result, InstrumentEventExDto?)> GetNextInstrumentEvent(CancellationToken ct) {
        lock (_data) {
            InstrumentEventExDto? bestPotentialEvent = _data.GetNextInstrumentEvent();
            return ValueTask.FromResult((Result.SUCCESS, bestPotentialEvent));
        }
    }

    public ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct) {
        lock (_data) {
            List<CurrentInstrumentRawDataReportDto> reports = _data.GetCurrentInstrumentReports(instrumentId);
            return ValueTask.FromResult<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)>((Result.SUCCESS, reports));
        }
    }

    public ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventExDto dto, CancellationToken ct) {
        lock (_data) {
            _data.MarkInstrumentEventAsProcessed(dto.InstrumentId, dto.EventType);
        }

        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct) {
        lock (_data) {
            _data.InsertProcessedCompanyReport(dto);
        }

        return ValueTask.FromResult(Result.SUCCESS);
    }

    #endregion

    #region RAW

    public ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct) {
        lock (_data) {
            IReadOnlyList<InstrumentDto> instrumentList = _data.GetInstrumentList();
            return ValueTask.FromResult<(Result, IReadOnlyList<InstrumentDto>)>((Result.SUCCESS, instrumentList));
        }
    }

    public ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct) {
        lock (_data) {
            _data.UpdateInstrumentList(newInstrumentList, obsoletedInstrumentList);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, ApplicationCommonState?)> GetApplicationCommonState(CancellationToken ct) {
        lock (_data) {
            ApplicationCommonState? stateFsmState = _data.GetApplicationCommonState();
            Result res = stateFsmState == null ? Result.SetFailure("No state found") : Result.SUCCESS;
            return ValueTask.FromResult<(Result, ApplicationCommonState?)>((res, stateFsmState));
        }
    }

    public ValueTask<Result> PersistStateFsmState(ApplicationCommonState stateFsmState, CancellationToken ct) {
        lock (_data) {
            _data.SetStateFsmState(stateFsmState);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct) {
        lock (_data) {
            _data.UpdateNextTimeToFetchQuote(nextTimeToFetchQuotes);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct) {
        lock (_data) {
            IReadOnlyList<CurrentInstrumentRawDataReportDto> rawFinancialsList = _data.GetRawFinancialsByInstrumentId(instrumentId);
            return ValueTask.FromResult((Result.SUCCESS, rawFinancialsList));
        }
    }

    public ValueTask<Result> UpdateRawInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct) {
        lock (_data) {
            _data.UpdateInstrumentReports(rawFinancialsDelta);
            return ValueTask.FromResult(Result.SUCCESS);
        }
    }

    public ValueTask<Result<PagedInstrumentsWithRawDataReportUpdatesDto>> GetRawInstrumentsWithUpdatedDataReports(string exchange, int pageNumber, int pageSize, CancellationToken ct) {
        lock (_data) {
            PagedInstrumentsWithRawDataReportUpdatesDto rawInstrumentReports = _data.GetRawInstrumentsWithUpdatedDataReports(exchange, pageNumber, pageSize);
            return ValueTask.FromResult(new Result<PagedInstrumentsWithRawDataReportUpdatesDto>(true, string.Empty, rawInstrumentReports));
        }
    }

    public ValueTask<Result<PagedInstrumentInfoDto>> GetInstrumentsWithNoRawReports(string exchange, int pageNumber, int pageSize, CancellationToken ct) {
        var emptyResult = PagedInstrumentInfoDto.WithPageNumberAndSizeOnly(pageNumber, pageSize);
        return ValueTask.FromResult(new Result<PagedInstrumentInfoDto>(true, string.Empty, emptyResult));
    }

    public ValueTask<Result> IgnoreRawUpdatedDataReport(RawInstrumentReportsToKeepAndIgnoreDto dto, CancellationToken ct) {
        lock (_data) {
            var res = _data.IgnoreRawUpdatedDataReport(dto);
            return ValueTask.FromResult(res);
        }
    }

    public ValueTask<(Result res, bool existsMatching)> ExistsMatchingRawReport(CurrentInstrumentRawDataReportDto dto, CancellationToken ct) {
        lock (_data) {
            bool foundMatch = _data.ExistsMatchingRawReport(dto);
            return ValueTask.FromResult((Result.SUCCESS, foundMatch));
        }
    }

    public ValueTask<Result> UpsertRawCurrentInstrumentReport(CurrentInstrumentRawDataReportDto rawReportData, CancellationToken ct)
        => throw new NotImplementedException();

    #endregion

    #region Data Requests

    public ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct) {
        lock (_data) {
            IReadOnlyList<ProcessedFullInstrumentReportDto> processedInstrumentReports = _data.GetProcessedStockDataByExchange(exchange);
            return ValueTask.FromResult(new Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>(true, string.Empty, processedInstrumentReports));
        }
    }

    public ValueTask<Result<ProcessedFullInstrumentReportDto>> GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol, CancellationToken ct) {
        lock (_data) {
            ProcessedFullInstrumentReportDto? processedInstrumentReport = _data.GetProcessedStockDataByExchangeAndSymbol(exchange, instrumentSymbol);
        }
        return ValueTask.FromResult(new Result<ProcessedFullInstrumentReportDto>(true, string.Empty, null));
    }

    #endregion

    #region Dashboard

    public ValueTask<(Result, DashboardStatsDto?)> GetDashboardStats(CancellationToken ct) {
        lock (_data) {
            DashboardStatsDto dto = _data.GetDashboardStats();
            return ValueTask.FromResult<(Result, DashboardStatsDto?)>((Result.SUCCESS, dto));
        }
    }

    #endregion

    #region Service State

    public ValueTask<(Result, bool)> GetCommonServiceState(string serviceName, CancellationToken ct) {
        lock (_data) {
            bool isPaused = _data.GetCommonServiceState(serviceName);
            return ValueTask.FromResult((Result.SUCCESS, isPaused));
        }
    }

    public ValueTask<Result> PersistCommonServiceState(bool isPaused, string serviceName, CancellationToken ct) {
        lock (_data) {
            _data.SetCommonServiceState(isPaused, serviceName);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

    #endregion
}
