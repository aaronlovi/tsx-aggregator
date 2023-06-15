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

    public DbmInMemory(IServiceProvider svp) {
		_logger = svp.GetRequiredService<ILogger<DbmInMemory>>();
		_logger.LogWarning("DbmInMemory is instantiated: persistence in RAM only");
		_lock = new();
	}

	public ValueTask<ulong> GetNextId64(CancellationToken ct) {
		return GetIdRange64(1, ct);
	}

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

    public ValueTask<(Result, InstrumentEventDto?)> GetNextInstrumentEvent(CancellationToken ct) {
        return ValueTask.FromResult<(Result, InstrumentEventDto?)>((Result.SUCCESS, null));
    }

    public ValueTask<(Result, IReadOnlyList<InstrumentReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct) {
        return ValueTask.FromResult<(Result, IReadOnlyList<InstrumentReportDto>)>((Result.SUCCESS, Array.Empty<InstrumentReportDto>()));
    }

    public ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventDto dto, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> InsertInstrumentIfNotExists(string companySymbol, string companyName, string instrumentSymbol, string instrumentName, string exchange, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> ObsoleteInstrument(string companySymbol, string instrumentSymbol, string exchange, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct) {
        return ValueTask.FromResult<(Result, IReadOnlyList<InstrumentDto>)>((Result.SUCCESS, Array.Empty<InstrumentDto>()));
    }

    public ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result> InsertInstrumentEvent(InstrumentEventDto instrumentEventDto, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, StateFsmState?)> GetStateFsmState(CancellationToken ct) {
        return ValueTask.FromResult<(Result, StateFsmState?)>((Result.SUCCESS, null));
    }

    public ValueTask<Result> PersistStateFsmState(StateFsmState stateFsmState, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<(Result, IReadOnlyList<InstrumentReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct) {
        return ValueTask.FromResult<(Result, IReadOnlyList<InstrumentReportDto>)>((Result.SUCCESS, Array.Empty<InstrumentReportDto>()));
    }

    public ValueTask<Result> UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct) {
        return ValueTask.FromResult(Result.SUCCESS);
    }

    public ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct) {
        return ValueTask.FromResult(new Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>(true, string.Empty, Array.Empty<ProcessedFullInstrumentReportDto>()));
    }
}
