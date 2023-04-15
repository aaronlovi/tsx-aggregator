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

    public GetInstrumentListStmt() : base(sql, nameof(GetInstrumentListStmt)) {
        _instrumentDtoList = new();
    }

    public IReadOnlyList<InstrumentDto> Instruments => _instrumentDtoList;

    protected override void ClearResults() {
        _instrumentDtoList.Clear();
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return Array.Empty<NpgsqlParameter>();
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        var i = new InstrumentDto(
            (ulong)reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDateTime(6).EnsureUtc(),
            null);
        _instrumentDtoList.Add(i);
        return true;
    }
}
