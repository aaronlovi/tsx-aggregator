﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

public interface IDbmService {
    // id generator
    ValueTask<ulong> GetNextId64(CancellationToken ct);
    ValueTask<ulong> GetIdRange64(uint count, CancellationToken ct);

    // Aggregated
    ValueTask<(Result, InstrumentEventDto?)> GetNextInstrumentEvent(CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<InstrumentReportDto>)> GetCurrentInstrumentReports(long instrumentId, CancellationToken ct);
    ValueTask<Result> MarkInstrumentEventAsProcessed(InstrumentEventDto dto, CancellationToken ct);
    ValueTask<Result> InsertProcessedCompanyReport(ProcessedInstrumentReportDto dto, CancellationToken ct);

    // Raw
    ValueTask<Result> InsertInstrumentIfNotExists(string companySymbol, string companyName, string instrumentSymbol, string instrumentName, string exchange, CancellationToken ct);
    ValueTask<Result> ObsoleteInstrument(string companySymbol, string instrumentSymbol, string exchange, CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<InstrumentDto>)> GetInstrumentList(CancellationToken ct);
    ValueTask<Result> UpdateInstrumentList(IReadOnlyList<InstrumentDto> newInstrumentList, IReadOnlyList<InstrumentDto> obsoletedInstrumentList, CancellationToken ct);
    ValueTask<Result> InsertInstrumentEvent(InstrumentEventDto instrumentEventDto, CancellationToken ct);
    ValueTask<(Result, StateFsmState?)> GetStateFsmState(CancellationToken ct);
    ValueTask<Result> PersistStateFsmState(StateFsmState stateFsmState, CancellationToken ct);
    ValueTask<(Result, IReadOnlyList<InstrumentReportDto>)> GetRawFinancialsByInstrumentId(long instrumentId, CancellationToken ct);
    ValueTask<Result> UpdateInstrumentReports(RawFinancialsDelta rawFinancialsDelta, CancellationToken ct);
}
