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
        _instrumentEventsByInstrumentId = [];
        _instrumentsByInstrumentId = [];
        _pricesByInstrumentId = [];
        _rawDataReportsByInstrumentId = [];
        _processedInstrumentReportsByInstrumentId = [];
        _serviceIsPausedByName = [];
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

            var currentReport = new CurrentInstrumentRawDataReportDto(
                instrumentReport.InstrumentReportId,
                instrumentId,
                instrumentReport.ReportType,
                instrumentReport.ReportPeriodType,
                instrumentReport.ReportJson,
                instrumentReport.ReportDate);
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

            var currentReport = new CurrentInstrumentRawDataReportDto(
                instrumentReport.InstrumentReportId,
                instrumentId,
                instrumentReport.ReportType,
                instrumentReport.ReportPeriodType,
                instrumentReport.ReportJson,
                instrumentReport.ReportDate);
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
                        || !ir.IsCurrent)
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

                    _ = numAnnualCashFlowReportsByInstrumentId.TryGetValue(instrument.InstrumentId, out int numAnnualCashFlowReports);
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
                        || !ir.IsCurrent)
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
            instrumentEvents = _instrumentEventsByInstrumentId[dto.InstrumentId] = [];

        instrumentEvents.Add(dto);
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

        // Insert new instrument reports
        foreach (var newReport in instrumentReportsToInsert) {
            if (!_rawDataReportsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
                instrumentReports = _rawDataReportsByInstrumentId[instrumentId] = [];

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
                IsCurrent: true));
        }

        // Update existing instrument reports with merged JSON
        foreach (var reportUpdate in rawFinancialsDelta.InstrumentReportsToUpdate) {
            if (!_rawDataReportsByInstrumentId.TryGetValue(instrumentId, out List<InstrumentRawDataReportDto>? instrumentReports))
                continue;

            for (int i = 0; i < instrumentReports.Count; i++) {
                if (instrumentReports[i].InstrumentReportId != reportUpdate.InstrumentReportId)
                    continue;

                instrumentReports[i] = instrumentReports[i] with { ReportJson = reportUpdate.MergedReportJson, CreatedDate = utcNow };
            }
        }

        // Insert raw data changed event, if needed
        if (instrumentReportsToInsert.Count > 0 || instrumentReportsToObsolete.Count > 0
            || rawFinancialsDelta.InstrumentReportsToUpdate.Count > 0) {
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
            instrumentPrices = _pricesByInstrumentId[instrumentId] = [];
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
            instrumentReports = _processedInstrumentReportsByInstrumentId[dto.InstrumentId] = [];

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
        var reportCountsByType = new Dictionary<int, long>();
        foreach (var entry in _rawDataReportsByInstrumentId) {
            foreach (InstrumentRawDataReportDto report in entry.Value) {
                if (latestRaw is null || report.CreatedDate > latestRaw)
                    latestRaw = report.CreatedDate;

                if (report.IsCurrent) {
                    _ = reportCountsByType.TryGetValue(report.ReportType, out long cnt);
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
            RawReportCountsByType: rawReportCounts);
    }

    internal void SetCommonServiceState(bool isPaused, string serviceName) =>
        _serviceIsPausedByName[serviceName] = isPaused;
}
