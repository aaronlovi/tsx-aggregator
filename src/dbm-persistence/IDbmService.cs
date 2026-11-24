using System;
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
    /// <summary>Listed instruments, with raw-data and pending-RawDataChanged flags (tsx-ops requeue).</summary>
    ValueTask<(Result, IReadOnlyList<RequeueCandidateDto>)> GetRequeueCandidates(CancellationToken ct);
    /// <summary>Queues one unprocessed RawDataChanged event per instrument, so the Aggregator rebuilds its report.</summary>
    ValueTask<Result> InsertRawDataChangedEvents(IReadOnlyCollection<long> instrumentIds, CancellationToken ct);
    /// <summary>
    /// Current raw reports of listed instruments that share a calendar year (annual) or quarter (others) with another
    /// current report of their type, or whose JSON has another report date (tsx-ops merged-reports).
    /// </summary>
    ValueTask<(Result, IReadOnlyList<MergedReportCandidateDto>)> GetMergedReportCandidates(CancellationToken ct);
    /// <summary>
    /// Marks these raw reports as not current, and queues one RawDataChanged event per instrument that lost a report,
    /// in one statement. The next scrape inserts a clean copy of those TMX still sends.
    /// </summary>
    ValueTask<Result> ResetRawReports(IReadOnlyCollection<long> instrumentReportIds, CancellationToken ct);

    // Raw
    ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct);
    ValueTask<(Result, IReadOnlyDictionary<InstrumentKey, long>)> GetObsoletedInstrumentIds(CancellationToken ct);
    ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> revivedInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct);
    ValueTask<(Result, ApplicationCommonState?)> GetApplicationCommonState(CancellationToken ct);
    ValueTask<Result> PersistStateFsmState(ApplicationCommonState stateFsmState, CancellationToken ct);
    ValueTask<Result> UpdateNextTimeToFetchQuotes(DateTime nextTimeToFetchQuotes, CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<CurrentInstrumentRawDataReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct);
    ValueTask<Result> UpdateRawInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct);
    /// <summary>
    /// After a successful scrape: stamps last_scraped_date, and stores TMX's sector and industry when the scrape had
    /// them (a null keeps the stored value).
    /// </summary>
    ValueTask<Result> UpdateInstrumentScrapeResult(long instrumentId, DateTimeOffset lastScrapedDate, string? sector, string? industry, CancellationToken ct);

    ValueTask<Result<PagedInstrumentInfoDto>> GetInstrumentsWithNoRawReports(string exchange, int pageNumber, int pageSize, CancellationToken ct);

    // Data Requests
    ValueTask<Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>> GetProcessedStockDataByExchange(string exchange, CancellationToken ct);
    ValueTask<Result<ProcessedFullInstrumentReportDto>> GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol, CancellationToken ct);

    // Dashboard
    ValueTask<(Result, DashboardStatsDto?)> GetDashboardStats(CancellationToken ct);

    // Service State
    ValueTask<(Result, bool)> GetCommonServiceState(string serviceName, CancellationToken ct);
    ValueTask<Result> PersistCommonServiceState(bool isPaused, string serviceName, CancellationToken ct);
}
