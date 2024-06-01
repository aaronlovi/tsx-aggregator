using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace dbm_persistence;
internal class GetInstrumentBySymbolAndExchangeStmt : QueryDbStmtBase {
    private const string sql = "SELECT instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date, obsoleted_date"
        + " FROM instruments"
        + " WHERE company_symbol = @company_symbol"
        + " AND instrument_symbol = @instrument_symbol"
        + " AND exchange = @exchange"
        + " AND obsoleted_date IS null"
        + " ORDER BY created_date DESC"
        + " LIMIT 1";

    // Inputs
    private readonly string _companySymbol;
    private readonly string _instrumentSymbol;
    private readonly string _exchange;

    // Results
    private InstrumentDto? _instrumentDto;

    public GetInstrumentBySymbolAndExchangeStmt(string companySymbol, string instrumentSymbol, string exchange)
        : base(sql, nameof(GetInstrumentBySymbolAndExchangeStmt)) 
    {
        _companySymbol = companySymbol;
        _instrumentSymbol = instrumentSymbol;
        _exchange = exchange;
    }

    public InstrumentDto? Results => _instrumentDto;

    protected override void ClearResults() => _instrumentDto = null;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<string>("company_symbol", _companySymbol),
            new NpgsqlParameter<string>("instrument_symbol", _instrumentSymbol),
            new NpgsqlParameter<string>("exchange", _exchange)
        };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        _instrumentDto = new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDateTime(5).EnsureUtc(),
            null);
        return false;
    }
}
