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

    private ulong GetNextId64() => GetIdRange64(1);

    private ulong GetIdRange64(uint count) {
        if (count == 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be 0");

        // Always in memory
        lock (_lock) {
            var result = _lastUsed + 1;
            _lastUsed += count;
            return result;
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

    public ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct) {
        lock (_data) {
            List<CurrentInstrumentReportDto> reports = _data.GetCurrentInstrumentReports(instrumentId);
            return ValueTask.FromResult<(Result, IReadOnlyList<CurrentInstrumentReportDto>)>((Result.SUCCESS, reports));
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

    public ValueTask<Result> InsertInstrumentIfNotExists(string companySymbol, string companyName, string instrumentSymbol, string instrumentName, string exchange, CancellationToken ct) {
        lock (_data) {
            InstrumentDto? instrument = _data.GetInstrumentBySymbolAndExchange(companySymbol, instrumentSymbol, exchange);

            if (instrument is not null)
                return ValueTask.FromResult(Result.SetFailure($"Instrument[{companySymbol},{companyName},{instrumentSymbol}] already exists"));

            long instrumentId = (long)GetNextId64();
            var instrumentDto = new InstrumentDto(instrumentId, exchange, companySymbol, companyName, instrumentSymbol, instrumentName, DateTime.UtcNow, null);
            _data.InsertInstrument(instrumentDto);
        }

        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> ObsoleteInstrument(string companySymbol, string instrumentSymbol, string exchange, CancellationToken ct) {
        lock (_data) {
            InstrumentDto? instrument = _data.GetInstrumentBySymbolAndExchange(companySymbol, instrumentSymbol, exchange);

            if (instrument is not null)
                _data.ObsoleteInstrument(instrument.InstrumentId, DateTimeOffset.UtcNow);
        }

        return ValueTask.FromResult(Result.SUCCESS);
    }

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

    public ValueTask<Result> InsertInstrumentEvent(InstrumentEventExDto instrumentEventDto, CancellationToken ct) {
        lock (_data) {
            _data.InsertInstrumentEvent(instrumentEventDto.InstrumentEvent);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, StateFsmState?)> GetStateFsmState(CancellationToken ct) {
        lock (_data) {
            StateFsmState? stateFsmState = _data.GetStateFsmState();
            Result res = stateFsmState == null ? Result.SetFailure("No state found") : Result.SUCCESS;
            return ValueTask.FromResult<(Result, StateFsmState?)>((res, stateFsmState));
        }
    }

    public ValueTask<Result> PersistStateFsmState(StateFsmState stateFsmState, CancellationToken ct) {
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

    public ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct) {
        lock (_data) {
            IReadOnlyList<CurrentInstrumentReportDto> rawFinancialsList = _data.GetRawFinancialsByInstrumentId(instrumentId);
            return ValueTask.FromResult((Result.SUCCESS, rawFinancialsList));
        }
    }

    public ValueTask<Result> UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct) {
        lock (_data) {
            _data.UpdateInstrumentReports(rawFinancialsDelta);
        }
        return ValueTask.FromResult(Result.SUCCESS);
    }

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
}
