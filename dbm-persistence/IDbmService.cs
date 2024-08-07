﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

public interface IDbmService {
    public const string TsxScraperConnStringName = "tsx-scraper";

    // id generator
    ValueTask<ulong> GetNextId64(CancellationToken ct);
    ValueTask<ulong> GetIdRange64(uint count, CancellationToken ct);

    // Aggregated
    ValueTask<(Result, InstrumentEventExDto?)> GetNextInstrumentEvent(CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct);
    ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventExDto dto, CancellationToken ct);
    ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct);

    // Raw
    ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct);
    ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct);
    ValueTask<(Result, ApplicationCommonState?)> GetApplicationCommonState(CancellationToken ct);
    ValueTask<Result> PersistStateFsmState(ApplicationCommonState stateFsmState, CancellationToken ct);
    ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<CurrentInstrumentReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct);
    ValueTask<Result> UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct);

    // Data Requests
    ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct);
    ValueTask<Result<ProcessedFullInstrumentReportDto>> GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol, CancellationToken ct);

    // Service State
    ValueTask<(Result, bool)> GetCommonServiceState(string serviceName, CancellationToken ct);
    ValueTask<Result> PersistCommonServiceState(bool isPaused, string serviceName, CancellationToken ct);
}
