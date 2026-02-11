using System;
using System.Collections.Generic;
using System.Text.Json;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal class DbmInMemoryData {
    private readonly Dictionary<long, List<InstrumentEventDto>> _instrumentEventsByInstrumentId;
    private readonly Dictionary<long, InstrumentDto> _instrumentsByInstrumentId;
    private readonly Dictionary<long, List<InstrumentPriceDto>> _pricesByInstrumentId;
    private readonly Dictionary<long, List<InstrumentRawDataReportDto>> _rawDataReportsByInstrumentId;
    private readonly Dictionary<long, List<ProcessedInstrumentReportDto>> _processedInstrumentReportsByInstrumentId;
    private readonly Dictionary<string, bool> _serviceIsPausedByName;
    private ApplicationCommonState? _stateFsmState;

    public DbmInMemoryData() {
        _instrumentEventsByInstrumentId = new();
        _instrumentsByInstrumentId = new();
        _pricesByInstrumentId = new();
        _rawDataReportsByInstrumentId = new();
        _processedInstrumentReportsByInstrumentId = new();
        _serviceIsPausedByName = new();
    }

    public InstrumentEventExDto? GetNextInstrumentEvent() {
        InstrumentEventExDto? bestPotentialEvent = null;

        foreach ((long instrumentId, List<InstrumentEventDto> instrumentEvents) in _instrumentEventsByInstrumentId) {

            // Instrument Id is fixed

            foreach (InstrumentEventDto instrumentEvent in instrumentEvents) {
                if (instrumentEvent.IsProcessed)
                    continue;

                // Instrument event is fixed

                if (bestPotentialEvent is not null
                    && instrumentEvent.EventDate >= bestPotentialEvent.EventDate) {
                    // Skip this event because we already have a better one,
                    // ordered by event date
                    continue;
                }

                if (!_instrumentsByInstrumentId.TryGetValue(instrumentId, out InstrumentDto? instrument))
                    continue;

                if (instrument.ObsoletedDate is not null)
                    continue;

                // Instrument is fixed

                if (!_pricesByInstrumentId.TryGetValue(instrumentId, out List<InstrumentPriceDto>? instrumentPrices))
                    continue;

                foreach (InstrumentPriceDto instrumentPrice in instrumentPrices) {
                    if (instrumentPrice.ObsoletedDate is not null)
                        continue;

                    // Instrument price is fixed

                    bestPotentialEvent = new InstrumentEventExDto(
                        instrumentEvent,
                        instrument.InstrumentSymbol,
                        instrument.InstrumentName,
                        instrument.Exchange,
                        instrumentPrice.PricePerShare,
                        instrumentPrice.NumShares);

                    // Minor optimization. There should never be two instrument price records
                    // for the same instrument with the same date that are both current.
                    // So we can break here.
                    break;
                }
            }
        }

        return bestPotentialEvent;
    }

    public List<CurrentInstrumentRawDataReportDto> GetCurrentInstrumentReports(long instrumentId) {
        var reports = new List<CurrentInstrumentRawDataReportDto>();

        if (!_rawDataReportsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
            return reports;

        foreach (InstrumentRawDataReportDto instrumentReport in instrumentReports) {
            if (!instrumentReport.IsCurrent)
                continue;

            if (instrumentReport.CheckManually)
                continue;

            var currentReport = new CurrentInstrumentRawDataReportDto(
                instrumentReport.InstrumentReportId,
                instrumentId,
                instrumentReport.ReportType,
                instrumentReport.ReportPeriodType,
                instrumentReport.ReportJson,
                instrumentReport.ReportDate,
                instrumentReport.CheckManually,
                instrumentReport.IgnoreReport);
            reports.Add(currentReport);
        }

        return reports;
    }

    public InstrumentDto? GetInstrumentBySymbolAndExchange(string companySymbol, string instrumentSymbol, string exchange) {
        foreach (InstrumentDto instrument in _instrumentsByInstrumentId.Values) {
            if (!instrument.CompanySymbol.EqualsInvariant(companySymbol))
                continue;
            if (!instrument.InstrumentSymbol.EqualsInvariant(instrumentSymbol))
                continue;
            if (!instrument.Exchange.EqualsInvariant(exchange))
                continue;
            if (instrument.ObsoletedDate is not null)
                continue;

            return instrument;
        }

        return null;
    }

    public IReadOnlyList<InstrumentDto> GetInstrumentList() {
        var results = new List<InstrumentDto>();

        foreach (InstrumentDto instrument in _instrumentsByInstrumentId.Values) {
            if (instrument.ObsoletedDate is not null)
                continue;

            results.Add(instrument);
        }

        return results;
    }

    public ApplicationCommonState? GetApplicationCommonState() => _stateFsmState == null ? null : new ApplicationCommonState(_stateFsmState);

    public IReadOnlyList<CurrentInstrumentRawDataReportDto> GetRawFinancialsByInstrumentId(long instrumentId) {
        var retVal = new List<CurrentInstrumentRawDataReportDto>();

        if (!_rawDataReportsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
            return retVal;

        foreach (InstrumentRawDataReportDto instrumentReport in instrumentReports) {
            if (!instrumentReport.IsCurrent)
                continue;

            if (instrumentReport.CheckManually)
                continue;

            var currentReport = new CurrentInstrumentRawDataReportDto(
                instrumentReport.InstrumentReportId,
                instrumentId,
                instrumentReport.ReportType,
                instrumentReport.ReportPeriodType,
                instrumentReport.ReportJson,
                instrumentReport.ReportDate,
                instrumentReport.CheckManually,
                instrumentReport.IgnoreReport);
            retVal.Add(currentReport);
        }

        return retVal;
    }

    public IReadOnlyList<ProcessedFullInstrumentReportDto> GetProcessedStockDataByExchange(string exchange) {
        var retVal = new List<ProcessedFullInstrumentReportDto>();
        var numAnnualCashFlowReportsByInstrumentId = new Dictionary<long, int>();

        foreach (InstrumentDto instrument in _instrumentsByInstrumentId.Values) {
            if (instrument.ObsoletedDate is not null || !instrument.Exchange.EqualsInvariant(exchange))
                continue;

            // Instrument is fixed

            if (!_processedInstrumentReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out List<ProcessedInstrumentReportDto>? processedInstrumentReports))
                continue;

            if (!_rawDataReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
                continue;

            foreach (ProcessedInstrumentReportDto pir in processedInstrumentReports) {
                if (pir.ReportObsoletedDate is not null)
                    continue;

                // Processed instrument report is fixed

                foreach (InstrumentRawDataReportDto ir in instrumentReports) {
                    if (ir.ReportType != (int)ReportTypes.CashFlow
                        || ir.ReportPeriodType != (int)ReportPeriodTypes.Annual
                        || !ir.IsCurrent
                        || ir.CheckManually)
                        continue;

                    // Instrument report is fixed

                    var processedFullInstrumentReport = new ProcessedFullInstrumentReportDto(
                        InstrumentId: instrument.InstrumentId,
                        CompanySymbol: instrument.CompanySymbol,
                        CompanyName: instrument.CompanyName,
                        InstrumentSymbol: instrument.InstrumentSymbol,
                        InstrumentName: instrument.InstrumentName,
                        SerializedReport: pir.SerializedReport,
                        InstrumentCreatedDate: instrument.CreatedDate,
                        InstrumentObsoletedDate: null,
                        ReportCreatedDate: pir.ReportCreatedDate,
                        ReportObsoletedDate: null,
                        NumAnnualCashFlowReports: 0); // Don't know yet
                    retVal.Add(processedFullInstrumentReport);

                    numAnnualCashFlowReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out int numAnnualCashFlowReports);
                    numAnnualCashFlowReportsByInstrumentId[instrument.InstrumentId] = numAnnualCashFlowReports + 1;
                }
            }
        }

        for (int i = 0; i < retVal.Count; i++) {
            if (!numAnnualCashFlowReportsByInstrumentId.TryGetValue(retVal[i].InstrumentId, out int numAnnualCashFlowReports))
                continue;

            retVal[i] = retVal[i] with { NumAnnualCashFlowReports = numAnnualCashFlowReports };
        }

        return retVal;
    }

    public ProcessedFullInstrumentReportDto? GetProcessedStockDataByExchangeAndSymbol(string exchange, string instrumentSymbol) {
        ProcessedFullInstrumentReportDto? retVal = null;
        var numAnnualCashFlowReports = 0;

        foreach (InstrumentDto instrument in _instrumentsByInstrumentId.Values) {
            if (instrument.ObsoletedDate is not null
                || !instrument.Exchange.EqualsInvariant(exchange)
                || !instrument.InstrumentSymbol.EqualsInvariant(instrumentSymbol))
                continue;

            // Instrument is fixed

            if (!_processedInstrumentReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out List<ProcessedInstrumentReportDto>? processedInstrumentReports))
                continue;

            if (!_rawDataReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
                continue;

            foreach (ProcessedInstrumentReportDto pir in processedInstrumentReports) {
                if (pir.ReportObsoletedDate is not null)
                    continue;

                // Processed instrument report is fixed

                retVal = new ProcessedFullInstrumentReportDto(
                    InstrumentId: instrument.InstrumentId,
                    CompanySymbol: instrument.CompanySymbol,
                    CompanyName: instrument.CompanyName,
                    InstrumentSymbol: instrument.InstrumentSymbol,
                    InstrumentName: instrument.InstrumentName,
                    SerializedReport: pir.SerializedReport,
                    InstrumentCreatedDate: instrument.CreatedDate,
                    InstrumentObsoletedDate: null,
                    ReportCreatedDate: pir.ReportCreatedDate,
                    ReportObsoletedDate: null,
                    NumAnnualCashFlowReports: 0); // Don't know yet

                foreach (InstrumentRawDataReportDto ir in instrumentReports) {
                    if (ir.ReportType != (int)ReportTypes.CashFlow
                        || ir.ReportPeriodType != (int)ReportPeriodTypes.Annual
                        || !ir.IsCurrent
                        || ir.CheckManually)
                        continue;

                    // Instrument report is fixed

                    ++numAnnualCashFlowReports;
                }
            }
        }

        if (retVal is not null)
            retVal = retVal with { NumAnnualCashFlowReports = numAnnualCashFlowReports };

        return retVal;
    }

    public bool GetCommonServiceState(string serviceName) {
        if (!_serviceIsPausedByName.TryGetValue(serviceName, out bool isPaused))
            return false; // Not paused by default
        return isPaused;
    }

    public PagedInstrumentsWithRawDataReportUpdatesDto GetRawInstrumentsWithUpdatedDataReports(
        string exchange, int pageNumber, int pageSize) {

        int totalItems = 0;
        var instrumentsWithUpdatedRawData = new List<InstrumentWithUpdatedRawDataDto>();
        int numToSkip = (pageNumber - 1) * pageSize;
        int numSkipped = 0;

        foreach (var entry in _rawDataReportsByInstrumentId) {
            long instrumentId = entry.Key;

            if (!_instrumentsByInstrumentId.TryGetValue(instrumentId, out InstrumentDto? instrument))
                continue;
            if (!instrument.Exchange.EqualsOrdinal(exchange))
                continue;

            // Instrument is fixed at this point

            List<InstrumentRawDataReportDto> rawDataReports = entry.Value;
            var rawDataReportsMap = new Dictionary<(DateOnly, ReportPeriodTypes), List<InstrumentRawDataReportDto>>();
            
            foreach (InstrumentRawDataReportDto rawDataReport in rawDataReports) {
                if (rawDataReport.ObsoletedDate is not null)
                    continue;
                if (rawDataReport.IgnoreReport)
                    continue;

                var key = (rawDataReport.ReportDate, (ReportPeriodTypes)rawDataReport.ReportPeriodType);
                if (!rawDataReportsMap.TryGetValue(key, out List<InstrumentRawDataReportDto>? reports))
                    rawDataReportsMap[key] = reports = new();
                reports.Add(rawDataReport);
            }

            foreach (var entry2 in rawDataReportsMap) {
                DateOnly reportDate = entry2.Key.Item1;
                ReportPeriodTypes reportPeriodType = entry2.Key.Item2;
                List<InstrumentRawDataReportDto> reports = entry2.Value;
                
                // Raw instrument data report does not have an update, skip
                if (reports.Count < 2)
                    continue;

                ++totalItems;

                // Skip until we reach the page we want
                if (numSkipped < numToSkip) {
                    ++numSkipped;
                    continue;
                }

                // Skip if we have reached the end of the page
                if (instrumentsWithUpdatedRawData.Count >= pageSize)
                    continue;

                // Instrument has updated data reports, and we are on the page we want

                var rawReportAndUpdates = new List<InstrumentWithUpdatedRawDataItemDto>();

                foreach (InstrumentRawDataReportDto report in reports) {
                    rawReportAndUpdates.Add(new InstrumentWithUpdatedRawDataItemDto(
                        InstrumentReportId: report.InstrumentReportId,
                        CreatedDate: report.CreatedDate,
                        IsCurrent: report.IsCurrent,
                        CheckManually: report.CheckManually,
                        IgnoreReport: report.IgnoreReport,
                        SerializedReport: report.ReportJson));
                }

                instrumentsWithUpdatedRawData.Add(new InstrumentWithUpdatedRawDataDto(
                    InstrumentId: instrumentId,
                    Exchange: instrument.Exchange,
                    CompanySymbol: instrument.CompanySymbol,
                    CompanyName: instrument.CompanyName,
                    InstrumentSymbol: instrument.InstrumentSymbol,
                    InstrumentName: instrument.InstrumentName,
                    ReportType: reports[0].ReportType,
                    ReportPeriodType: reports[0].ReportPeriodType,
                    ReportDate: reportDate,
                    RawReportAndUpdates: rawReportAndUpdates));
            }
        }

        return new PagedInstrumentsWithRawDataReportUpdatesDto(
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalInstruments: totalItems,
            InstrumentsWithUpdates: instrumentsWithUpdatedRawData);
    }

    public bool ExistsMatchingRawReport(CurrentInstrumentRawDataReportDto rawReportDto) {
        if (!_rawDataReportsByInstrumentId.TryGetValue(rawReportDto.InstrumentId, out List<InstrumentRawDataReportDto>? existingReports))
            return false;

        var newReportData = RawReportDataMap.FromJsonString(rawReportDto.ReportJson);

        foreach (InstrumentRawDataReportDto existingReport in existingReports) {
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
                return true;
        }

        return false;
    }

    public void MarkInstrumentEventAsProcessed(long instrumentId, int eventType) {
        if (!_instrumentEventsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentEventDto>? instrumentEvents))
            return;

        for (int i = 0; i < instrumentEvents.Count; i++) {
            if (instrumentEvents[i].EventType != eventType)
                continue;
            instrumentEvents[i] = instrumentEvents[i] with { IsProcessed = true };
        }
    }

    public void InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto) {
        ObsoleteOldProcessedInstrumentReports(dto);
        InsertProcessedInstrumentReport(dto);
        UpdateInstrumentEvents(dto.InstrumentId, true, (int)CompanyEventTypes.RawDataChanged);
    }

    public bool InsertInstrument(InstrumentDto dto) {
        if (_instrumentsByInstrumentId.ContainsKey(dto.InstrumentId))
            return false; // Nothing to insert. Not ideal, this should return a result indicating failure.

        _instrumentsByInstrumentId.Add(dto.InstrumentId, dto);

        return true;
    }

    public bool ObsoleteInstrument(long instrumentId, DateTimeOffset obsoletedDate) {
        if (!_instrumentsByInstrumentId.TryGetValue(instrumentId, out InstrumentDto? instrument))
            return false; // Nothing to obsolete

        _instrumentsByInstrumentId[instrumentId] = instrument with { ObsoletedDate = obsoletedDate };

        return true;
    }

    public void UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList) {
        foreach (InstrumentDto newInstrument in newInstrumentList) {
            var res = InsertInstrument(newInstrument);
            if (res) {
                var newInstrumentEvent = new InstrumentEventDto(newInstrument.InstrumentId, DateTime.UtcNow, (int)CompanyEventTypes.NewListedCompany, false);
                InsertInstrumentEvent(newInstrumentEvent);
            }
        }

        foreach (InstrumentDto instrumentDto in obsoletedInstrumentList) {
            var res = ObsoleteInstrument(instrumentDto.InstrumentId, DateTime.UtcNow);
            if (res) {
                var obsoletedInstrumentEvent = new InstrumentEventDto(instrumentDto.InstrumentId, DateTime.UtcNow, (int)CompanyEventTypes.ObsoletedListedCompany, false);
                InsertInstrumentEvent(obsoletedInstrumentEvent);
            }
        }
    }

    public void InsertInstrumentEvent(InstrumentEventDto dto) {
        if (!_instrumentEventsByInstrumentId.TryGetValue(dto.InstrumentId, out List<InstrumentEventDto>? instrumentEvents))
            instrumentEvents = _instrumentEventsByInstrumentId[dto.InstrumentId] = new();

        instrumentEvents.Add(dto);
    }

    public Result IgnoreRawUpdatedDataReport(RawInstrumentReportsToKeepAndIgnoreDto dto) {
        if (!_rawDataReportsByInstrumentId.TryGetValue(dto.InstrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
            return Result.SetFailure("Instrument not found");

        var consistencyMap = new RawReportConsistencyMap();
        RawReportConsistencyMapKey? mainKey = consistencyMap.BuildMap(dto, instrumentReports);

        if (mainKey is null)
            return Result.SetFailure("Report to keep not found");

        Result res = consistencyMap.EnsureRequestIsConsistent(dto, mainKey);
        if (!res.Success)
            return res;

        // If we got here, then the request is valid, so ignore the reports

        MarkReportsAsIgnored();

        return res;

        // Local helper methods

        void MarkReportsAsIgnored() {
            var instrumentReportIdsToIgnore = new HashSet<long>(dto.ReportIdsToIgnore);
            for (int i = 0; i < instrumentReports.Count; ++i) {
                InstrumentRawDataReportDto rawDataReport = instrumentReports[i];
                if (!instrumentReportIdsToIgnore.Contains(rawDataReport.InstrumentReportId))
                    continue;

                instrumentReports[i] = rawDataReport with { IgnoreReport = true, IsCurrent = false, CheckManually = false };
            }
        }
    }

    public void SetStateFsmState(ApplicationCommonState stateFsmState) => _stateFsmState = new ApplicationCommonState(stateFsmState);

    public void UpdateNextTimeToFetchQuote(DateTime nextTimeToFetchQuotes) {
        if (_stateFsmState == null)
            return;
        _stateFsmState.NextFetchQuotesTime = nextTimeToFetchQuotes;
    }

    public void UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta) {
        DateTime utcNow = DateTime.UtcNow;

        long instrumentId = rawFinancialsDelta.InstrumentId;
        decimal newPricePerShare = rawFinancialsDelta.PricePerShare;
        ulong newNumShares = rawFinancialsDelta.NumShares;

        IList<CurrentInstrumentRawDataReportDto> instrumentReportsToObsolete = rawFinancialsDelta.InstrumentReportsToObsolete;
        IList<CurrentInstrumentRawDataReportDto> instrumentReportsToInsert = rawFinancialsDelta.InstrumentReportsToInsert;

        // Obsolete old instrument reports
        foreach (var obsoletedReport in instrumentReportsToObsolete) {
            if (!_rawDataReportsByInstrumentId.TryGetValue(obsoletedReport.InstrumentReportId, out List<InstrumentRawDataReportDto>? instrumentReports))
                continue;

            for (int i = 0; i < instrumentReports.Count; i++) {
                if (instrumentReports[i].InstrumentReportId != obsoletedReport.InstrumentReportId)
                    continue;

                instrumentReports[i] = instrumentReports[i] with { ObsoletedDate = utcNow, IsCurrent = false };
            }
        }

        var numReportsToInsert = 0;
        var numReportsToCheckManually = 0;

        // Insert new instrument reports
        foreach (var newReport in instrumentReportsToInsert) {
            if (!_rawDataReportsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
                instrumentReports = _rawDataReportsByInstrumentId[instrumentId] = new();

            long newInstrumentReportId = newReport.InstrumentReportId;
            instrumentReports.Add(new InstrumentRawDataReportDto(
                newInstrumentReportId,
                instrumentId,
                newReport.ReportType,
                newReport.ReportPeriodType,
                newReport.ReportJson,
                newReport.ReportDate,
                CreatedDate: utcNow,
                ObsoletedDate: null,
                IsCurrent: true,
                CheckManually: newReport.CheckManually,
                IgnoreReport: newReport.IgnoreReport));

            numReportsToInsert += newReport.CheckManually ? 0 : 1;
            numReportsToCheckManually += newReport.CheckManually ? 1 : 0;
        }

        // Insert raw data changed event, if needed
        if (numReportsToInsert > 0 || instrumentReportsToObsolete.Count > 0) {
            var instrumentEventDto = new InstrumentEventDto(
                instrumentId,
                utcNow,
                EventType: (int)CompanyEventTypes.RawDataChanged,
                IsProcessed: false);
            InsertInstrumentEvent(instrumentEventDto);
        }

        AddNewInstrumentPrice(instrumentId, newPricePerShare, newNumShares, utcNow);
    }

    private void AddNewInstrumentPrice(long instrumentId, decimal newPricePerShare, ulong newNumShares, DateTime utcNow) {
        // Obsolete old instrument prices
        if (_pricesByInstrumentId.TryGetValue(instrumentId, out List<InstrumentPriceDto>? instrumentPrices)) {
            for (int i = 0; i < instrumentPrices.Count; i++) {
                instrumentPrices[i] = instrumentPrices[i] with { ObsoletedDate = utcNow };
            }
        }

        // Insert new instrument prices
        if (instrumentPrices == null) {
            instrumentPrices = _pricesByInstrumentId[instrumentId] = new();
            instrumentPrices.Add(new InstrumentPriceDto(
                instrumentId,
                newPricePerShare,
                (long)newNumShares,
                CreatedDate: utcNow,
                ObsoletedDate: null));
        }
    }

    private void ObsoleteOldProcessedInstrumentReports(ProcessedInstrumentReportDto dto) {
        if (!_processedInstrumentReportsByInstrumentId.TryGetValue(dto.InstrumentId, out List<ProcessedInstrumentReportDto>? instrumentReports))
            return; // Nothing to obsolete

        for (int i = 0; i < instrumentReports.Count; i++) {
            if (instrumentReports[i].ReportObsoletedDate is not null)
                continue;

            instrumentReports[i] = instrumentReports[i] with { ReportObsoletedDate = dto.ReportObsoletedDate ?? DateTime.UtcNow };
        }
    }

    private void InsertProcessedInstrumentReport(ProcessedInstrumentReportDto dto) {
        if (!_processedInstrumentReportsByInstrumentId.TryGetValue(dto.InstrumentId, out List<ProcessedInstrumentReportDto>? instrumentReports))
            instrumentReports = _processedInstrumentReportsByInstrumentId[dto.InstrumentId] = new();

        instrumentReports.Add(dto);
    }

    private void UpdateInstrumentEvents(long instrumentId, bool isProcessed, int eventType) {
        if (!_instrumentEventsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentEventDto>? instrumentEvents))
            return; // Nothing to update

        for (int i = 0; i < instrumentEvents.Count; i++) {
            if (instrumentEvents[i].EventType != eventType)
                continue;

            instrumentEvents[i] = instrumentEvents[i] with { IsProcessed = isProcessed };
        }
    }

    public DashboardStatsDto GetDashboardStats() {
        long totalActive = 0;
        long totalObsoleted = 0;
        foreach (InstrumentDto instrument in _instrumentsByInstrumentId.Values) {
            if (instrument.ObsoletedDate is null)
                ++totalActive;
            else
                ++totalObsoleted;
        }

        long withProcessed = 0;
        foreach (var entry in _processedInstrumentReportsByInstrumentId) {
            foreach (ProcessedInstrumentReportDto pir in entry.Value) {
                if (pir.ReportObsoletedDate is null) {
                    ++withProcessed;
                    break;
                }
            }
        }

        DateTimeOffset? latestRaw = null;
        long manualReview = 0;
        var reportCountsByType = new Dictionary<int, long>();
        foreach (var entry in _rawDataReportsByInstrumentId) {
            foreach (InstrumentRawDataReportDto report in entry.Value) {
                if (latestRaw is null || report.CreatedDate > latestRaw)
                    latestRaw = report.CreatedDate;

                if (report.IsCurrent && report.CheckManually)
                    ++manualReview;

                if (report.IsCurrent && !report.IgnoreReport) {
                    reportCountsByType.TryGetValue(report.ReportType, out long cnt);
                    reportCountsByType[report.ReportType] = cnt + 1;
                }
            }
        }

        DateTimeOffset? latestProcessed = null;
        foreach (var entry in _processedInstrumentReportsByInstrumentId) {
            foreach (ProcessedInstrumentReportDto pir in entry.Value) {
                if (latestProcessed is null || pir.ReportCreatedDate > latestProcessed)
                    latestProcessed = pir.ReportCreatedDate;
            }
        }

        long unprocessedEvents = 0;
        foreach (var entry in _instrumentEventsByInstrumentId) {
            foreach (InstrumentEventDto evt in entry.Value) {
                if (!evt.IsProcessed)
                    ++unprocessedEvents;
            }
        }

        var rawReportCounts = new List<RawReportCountByTypeDto>();
        foreach (var kvp in reportCountsByType)
            rawReportCounts.Add(new RawReportCountByTypeDto(kvp.Key, kvp.Value));
        rawReportCounts.Sort((a, b) => a.ReportType.CompareTo(b.ReportType));

        return new DashboardStatsDto(
            TotalActiveInstruments: totalActive,
            TotalObsoletedInstruments: totalObsoleted,
            InstrumentsWithProcessedReports: withProcessed,
            MostRecentRawIngestion: latestRaw,
            MostRecentAggregation: latestProcessed,
            UnprocessedEventCount: unprocessedEvents,
            ManualReviewCount: manualReview,
            RawReportCountsByType: rawReportCounts);
    }

    internal void SetCommonServiceState(bool isPaused, string serviceName) =>
        _serviceIsPausedByName[serviceName] = isPaused;
}
