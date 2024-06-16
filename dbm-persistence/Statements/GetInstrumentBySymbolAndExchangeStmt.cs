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

    private static int _instrumentIdIndex = -1;
    private static int _exchangeIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _companyNameIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _createdDateIndex = -1;

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

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
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
        _instrumentDto = new(
            reader.GetInt64(_instrumentIdIndex),
            reader.GetString(_exchangeIndex),
            reader.GetString(_companySymbolIndex),
            reader.GetString(_companyNameIndex),
            reader.GetString(_instrumentSymbolIndex),
            reader.GetString(_instrumentNameIndex),
            reader.GetDateTime(_createdDateIndex).EnsureUtc(),
            null);

        // Since we only expect one row, we return false to stop processing further rows.
        return false;
    }
}
