using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

/// <summary>
/// Loads the identity of every soft-deleted instrument, keyed by its natural key.
/// </summary>
/// <remarks>
/// The <c>uniq_instruments</c> constraint spans (exchange, company_symbol, instrument_symbol)
/// and ignores <c>obsoleted_date</c>, so an obsoleted row keeps occupying its natural key.
/// When an instrument gets re-listed we must reuse that row rather than insert a second one,
/// otherwise the insert collides and the instrument can never come back.
/// </remarks>
internal sealed class GetObsoletedInstrumentIdsStmt : QueryDbStmtBase {
    private const string sql = "SELECT instrument_id, exchange, company_symbol, instrument_symbol"
        + " FROM instruments"
        + " WHERE obsoleted_date IS NOT null";

    // Results
    private readonly Dictionary<InstrumentKey, long> _instrumentIdsByKey;

    private static int _instrumentIdIndex = -1;
    private static int _exchangeIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _instrumentSymbolIndex = -1;

    public GetObsoletedInstrumentIdsStmt() : base(sql, nameof(GetObsoletedInstrumentIdsStmt)) => _instrumentIdsByKey = [];

    public IReadOnlyDictionary<InstrumentKey, long> InstrumentIdsByKey => _instrumentIdsByKey;

    protected override void ClearResults() => _instrumentIdsByKey.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => [];

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentIdIndex != -1)
            return;

        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _exchangeIndex = reader.GetOrdinal("exchange");
        _companySymbolIndex = reader.GetOrdinal("company_symbol");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        var key = new InstrumentKey(
            reader.GetString(_companySymbolIndex),
            reader.GetString(_instrumentSymbolIndex),
            reader.GetString(_exchangeIndex));
        _instrumentIdsByKey[key] = reader.GetInt64(_instrumentIdIndex);
        return true;
    }
}
