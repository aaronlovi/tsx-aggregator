using System;
using System.Collections.Generic;
using tsx_aggregator.models;
using tsx_aggregator.shared;

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
                if (pir.ObsoletedDate is not null)
                    continue;

                // Processed instrument report is fixed

                foreach (InstrumentRawDataReportDto ir in instrumentReports) {
                    if (ir.ReportType != (int)Constants.ReportTypes.CashFlow
                        || ir.ReportPeriodType != (int)Constants.ReportPeriodTypes.Annual
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
                        ReportCreatedDate: pir.CreatedDate,
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
                if (pir.ObsoletedDate is not null)
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
                    ReportCreatedDate: pir.CreatedDate,
                    ReportObsoletedDate: null,
                    NumAnnualCashFlowReports: 0); // Don't know yet

                foreach (InstrumentRawDataReportDto ir in instrumentReports) {
                    if (ir.ReportType != (int)Constants.ReportTypes.CashFlow
                        || ir.ReportPeriodType != (int)Constants.ReportPeriodTypes.Annual
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
        UpdateInstrumentEvents(dto.InstrumentId, true, (int)Constants.CompanyEventTypes.RawDataChanged);
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
                var newInstrumentEvent = new InstrumentEventDto(newInstrument.InstrumentId, DateTime.UtcNow, (int)Constants.CompanyEventTypes.NewListedCompany, false);
                InsertInstrumentEvent(newInstrumentEvent);
            }
        }

        foreach (InstrumentDto instrumentDto in obsoletedInstrumentList) {
            var res = ObsoleteInstrument(instrumentDto.InstrumentId, DateTime.UtcNow);
            if (res) {
                var obsoletedInstrumentEvent = new InstrumentEventDto(instrumentDto.InstrumentId, DateTime.UtcNow, (int)Constants.CompanyEventTypes.ObsoletedListedCompany, false);
                InsertInstrumentEvent(obsoletedInstrumentEvent);
            }
        }
    }

    public void InsertInstrumentEvent(InstrumentEventDto dto) {
        if (!_instrumentEventsByInstrumentId.TryGetValue(dto.InstrumentId, out List<InstrumentEventDto>? instrumentEvents))
            instrumentEvents = _instrumentEventsByInstrumentId[dto.InstrumentId] = new();

        instrumentEvents.Add(dto);
    }

    public void IgnoreRawUpdatedDataReport(ulong instrumentReportId) {
        if (!_rawDataReportsByInstrumentId.TryGetValue((long)instrumentReportId, out List<InstrumentRawDataReportDto>? rawDataReports))
            return;

        for (int i = 0; i < rawDataReports.Count; i++) {
            if (rawDataReports[i].InstrumentReportId != (long)instrumentReportId)
                continue;

            rawDataReports[i] = rawDataReports[i] with { IgnoreReport = true };
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
                EventType: (int)Constants.CompanyEventTypes.RawDataChanged,
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
            if (instrumentReports[i].ObsoletedDate is not null)
                continue;

            instrumentReports[i] = instrumentReports[i] with { ObsoletedDate = dto.ObsoletedDate ?? DateTime.UtcNow };
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

    internal void SetCommonServiceState(bool isPaused, string serviceName) =>
        _serviceIsPausedByName[serviceName] = isPaused;
}
