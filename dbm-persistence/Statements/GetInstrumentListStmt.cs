using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;

internal sealed class GetInstrumentListStmt : QueryDbStmtBase {
    private const string sql = "SELECT instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date, obsoleted_date"
        + " FROM instruments"
        + " WHERE obsoleted_date IS null";

    // Results
    private readonly List<InstrumentDto> _instrumentDtoList;

    private static int _instrumentIdIndex = -1;
    private static int _exchangeIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _companyNameIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _createdDateIndex = -1;

    public GetInstrumentListStmt() : base(sql, nameof(GetInstrumentListStmt)) => _instrumentDtoList = new();

    public IReadOnlyList<InstrumentDto> Instruments => _instrumentDtoList;

    protected override void ClearResults() => _instrumentDtoList.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => Array.Empty<NpgsqlParameter>();

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentIdIndex != -1)
            return;

        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _exchangeIndex = reader.GetOrdinal("exchange");
        _companySymbolIndex = reader.GetOrdinal("company_symbol");
        _companyNameIndex = reader.GetOrdinal("company_name");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
        _instrumentNameIndex = reader.GetOrdinal("instrument_name");
        _createdDateIndex = reader.GetOrdinal("created_date");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        var instrumentDto = new InstrumentDto(
            reader.GetInt64(_instrumentIdIndex),
            reader.GetString(_exchangeIndex),
            reader.GetString(_companySymbolIndex),
            reader.GetString(_companyNameIndex),
            reader.GetString(_instrumentSymbolIndex),
            reader.GetString(_instrumentNameIndex),
            reader.GetDateTime(_createdDateIndex).EnsureUtc(),
            null);
        _instrumentDtoList.Add(instrumentDto);
        return true;
    }
}
