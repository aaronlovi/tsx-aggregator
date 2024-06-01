using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal class InsertInstrumentStmt : NonQueryDbStmtBase {
    private const string sql = "INSERT INTO instruments (instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date, obsoleted_date)"
        + " VALUES (@instrument_id, @exchange, @company_symbol, @company_name, @instrument_symbol, @instrument_name, @created_date, @obsoleted_date)";

    private readonly InstrumentDto _instrumentDto;

    public InsertInstrumentStmt(InstrumentDto instrumentDto)
        : base(sql, nameof(InsertInstrumentStmt)) => _instrumentDto = instrumentDto;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentDto.InstrumentId),
            new NpgsqlParameter<string>("exchange", _instrumentDto.Exchange),
            new NpgsqlParameter<string>("company_symbol", _instrumentDto.CompanySymbol),
            new NpgsqlParameter<string>("company_name", _instrumentDto.CompanyName),
            new NpgsqlParameter<string>("instrument_symbol", _instrumentDto.InstrumentSymbol),
            new NpgsqlParameter<string>("instrument_name", _instrumentDto.InstrumentName),
            new NpgsqlParameter<DateTime>("created_date", _instrumentDto.CreatedDate.UtcDateTime),
            DbUtils.CreateNullableDateTimeParam("obsoleted_date", _instrumentDto.ObsoletedDate)
        };
    }
}
