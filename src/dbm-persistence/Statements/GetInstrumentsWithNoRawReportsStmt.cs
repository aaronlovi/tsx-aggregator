using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetInstrumentsWithNoRawReportsStmt : QueryDbStmtBase {
    private static readonly string sql =
        "WITH instruments_no_raw AS ("
        + " SELECT i.instrument_id, i.exchange, i.company_symbol, i.instrument_symbol,"
        + " i.company_name, i.instrument_name,"
        + " ROW_NUMBER() OVER (ORDER BY i.instrument_symbol) as row_num"
        + " FROM instruments i"
        + " LEFT JOIN instrument_reports ir ON i.instrument_id = ir.instrument_id"
        + " WHERE i.obsoleted_date IS NULL"
        + " AND i.exchange = @exchange"
        + " AND ir.instrument_id IS NULL"
        + " ),"
        + " total AS (SELECT COALESCE(MAX(row_num), 0) as total_count FROM instruments_no_raw)"
        + " SELECT inr.instrument_id, inr.exchange, inr.company_symbol, inr.instrument_symbol,"
        + " inr.company_name, inr.instrument_name, t.total_count"
        + " FROM instruments_no_raw inr"
        + " CROSS JOIN total t"
        + " WHERE inr.row_num BETWEEN @rank_min AND @rank_max"
        + " ORDER BY inr.row_num";

    private readonly string _exchange;
    private readonly int _pageNumber;
    private readonly int _pageSize;
    private readonly int _minRank;
    private readonly int _maxRank;

    private static int _instrumentIdIndex = -1;
    private static int _exchangeIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _companyNameIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _totalCountIndex = -1;

    public GetInstrumentsWithNoRawReportsStmt(string exchange, int pageNumber, int pageSize)
        : base(sql, nameof(GetInstrumentsWithNoRawReportsStmt)) {
        _exchange = exchange;
        _pageNumber = pageNumber;
        _pageSize = pageSize;
        _minRank = (pageNumber - 1) * pageSize + 1;
        _maxRank = pageNumber * pageSize;
        PagedInstrumentInfo = PagedInstrumentInfoDto.WithPageNumberAndSizeOnly(pageNumber, pageSize);
    }

    public PagedInstrumentInfoDto PagedInstrumentInfo { get; private set; }

    protected override void ClearResults() =>
        PagedInstrumentInfo = PagedInstrumentInfoDto.WithPageNumberAndSizeOnly(_pageNumber, _pageSize);

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        new List<NpgsqlParameter> {
            new NpgsqlParameter<string>("exchange", _exchange),
            new NpgsqlParameter<int>("rank_min", _minRank),
            new NpgsqlParameter<int>("rank_max", _maxRank),
        };

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);
        if (_instrumentIdIndex != -1) return;

        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _exchangeIndex = reader.GetOrdinal("exchange");
        _companySymbolIndex = reader.GetOrdinal("company_symbol");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
        _companyNameIndex = reader.GetOrdinal("company_name");
        _instrumentNameIndex = reader.GetOrdinal("instrument_name");
        _totalCountIndex = reader.GetOrdinal("total_count");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        if (!PagedInstrumentInfo.IsValid) {
            int totalCount = reader.GetInt32(_totalCountIndex);
            PagedInstrumentInfo = new PagedInstrumentInfoDto(
                _pageNumber, _pageSize, totalCount, new List<InstrumentInfoDto>());
        }

        var dto = new InstrumentInfoDto(
            InstrumentId: reader.GetInt64(_instrumentIdIndex),
            Exchange: reader.GetString(_exchangeIndex),
            CompanySymbol: reader.GetString(_companySymbolIndex),
            InstrumentSymbol: reader.GetString(_instrumentSymbolIndex),
            CompanyName: reader.GetString(_companyNameIndex),
            InstrumentName: reader.GetString(_instrumentNameIndex));

        PagedInstrumentInfo.Instruments.Add(dto);
        return true;
    }
}
