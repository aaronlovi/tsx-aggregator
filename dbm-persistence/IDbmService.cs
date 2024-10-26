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
    ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct);
    ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventExDto dto, CancellationToken ct);
    ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct);

    // Raw
    ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct);
    ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct);
    ValueTask<(Result, ApplicationCommonState?)> GetApplicationCommonState(CancellationToken ct);
    ValueTask<Result> PersistStateFsmState(ApplicationCommonState stateFsmState, CancellationToken ct);
    ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct);
    ValueTask<Result> UpdateRawInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct);
    
    /// <summary>
    /// Get the list of instruments that have updated data reports.
    /// These either have:
    /// - 'check_manually' set to true, and also 'ignore' set to false.
    /// - obsoleted date set to false (i.e., the original report)
    /// </summary>
    ValueTask<Result<PagedInstrumentsWithRawDataReportUpdatesDto>> GetRawInstrumentsWithUpdatedDataReports(string exchange, int pageNumber, int pageSize, CancellationToken ct);

    /// <summary>
    /// Operation to ignore updated raw data reports.
    /// This may be necessary if the incoming data is bad, missing a lot of values, etc.
    /// </summary>
    ValueTask<Result> IgnoreRawUpdatedDataReport(RawInstrumentReportsToKeepAndIgnoreDto dto, CancellationToken ct);

    /// <summary>
    /// Insert or update a current instrument report.
    /// For example, when updating the 'ignore' flag.
    /// Written as 'upsert' rather than 'update' just in case there is no such record (for example, missed in replication).
    /// </summary>
    ValueTask<Result> UpsertRawCurrentInstrumentReport(CurrentInstrumentRawDataReportDto rawReportData, CancellationToken ct);

    // Data Requests
    ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct);
    ValueTask<Result<ProcessedFullInstrumentReportDto>> GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol, CancellationToken ct);

    // Service State
    ValueTask<(Result, bool)> GetCommonServiceState(string serviceName, CancellationToken ct);
    ValueTask<Result> PersistCommonServiceState(bool isPaused, string serviceName, CancellationToken ct);
}
