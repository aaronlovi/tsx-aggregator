using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetRawInstrumentsWithUpdatedDataReportsStmt : QueryDbStmtBase {
    private const string sql =
    " WITH ranked_reports AS("
    + " WITH duplicate_reports AS ("
    + " SELECT ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_date,"
    + " i.instrument_symbol, i.instrument_name, i.company_symbol, i.company_name"
    + " FROM instrument_reports ir"
    + " JOIN instruments i ON i.instrument_id = ir.instrument_id"
    + " WHERE ir.obsoleted_date IS NULL"
    + " AND i.obsoleted_date IS NULL"
    + " AND i.exchange = @exchange"
    + " AND ir.ignore_report = FALSE"
    + " GROUP BY ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_date,"
    + " i.instrument_symbol, i.instrument_name, i.company_symbol, i.company_name"
    + " HAVING COUNT(*) > 1"
    + " )"
    + " SELECT DENSE_RANK() OVER(ORDER BY ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_date) AS rank_num,"
    + " ir.instrument_id, ir.instrument_report_id, ir.report_type, ir.report_period_type, ir.report_json,"
    + " ir.report_date, ir.created_date, ir.is_current, ir.check_manually, ir.ignore_report,"
    + " dr.instrument_symbol, dr.instrument_name, dr.company_symbol, dr.company_name"
    + " FROM instrument_reports ir"
    + " JOIN duplicate_reports dr"
    + " ON ir.instrument_id = dr.instrument_id"
    + " AND ir.report_type = dr.report_type"
    + " AND ir.report_period_type = dr.report_period_type"
    + " AND ir.report_date = dr.report_date"
    + " ORDER BY rank_num, dr.instrument_symbol, dr.instrument_name, dr.company_symbol, dr.company_name,"
    + " ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_date, ir.created_date, ir.is_current, ir.check_manually,"
    + " ir.ignore_report),"
    + " max_rank AS(SELECT MAX(rank_num) AS max_rank_num FROM ranked_reports)"
    + " SELECT rr.rank_num,"
    + " rr.instrument_symbol, rr.instrument_name, rr.company_symbol, rr.company_name,"
    + " rr.instrument_id, rr.instrument_report_id, rr.report_type, rr.report_period_type,"
    + " rr.report_json, rr.report_date, rr.created_date, rr.is_current, rr.check_manually,"
    + " rr.ignore_report, mr.max_rank_num"
    + " FROM ranked_reports rr CROSS JOIN max_rank mr"
    + " WHERE rr.rank_num BETWEEN @rank_min AND @rank_max"
    + " ORDER BY rr.rank_num";

    private static int _rankNumIndex = -1;
    private static int _instrumentSymbolIndex = -1;
    private static int _instrumentNameIndex = -1;
    private static int _companySymbolIndex = -1;
    private static int _companyNameIndex = -1;
    private static int _instrumentIdIndex = -1;
    private static int _instrumentReportIdIndex = -1;
    private static int _reportTypeIndex = -1;
    private static int _reportPeriodTypeIndex = -1;
    private static int _reportJsonIndex = -1;
    private static int _reportDateIndex = -1;
    private static int _createdDateIndex = -1;
    private static int _isCurrentIndex = -1;
    private static int _checkManuallyIndex = -1;
    private static int _ignoreReportIndex = -1;
    private static int _maxRankNumIndex = -1;

    private readonly string _exchange;
    private readonly int _pageNumber;
    private readonly int _pageSize;
    private readonly int _minRank;
    private readonly int _maxRank;
    private int _curRankNum;

    public PagedInstrumentsWithRawDataReportUpdatesDto PagedInstrumentsWithRawDataReportUpdates { get; private set; }

    public GetRawInstrumentsWithUpdatedDataReportsStmt(string exchange, int pageNumber, int pageSize)
        : base(sql, nameof(GetRawInstrumentsWithUpdatedDataReportsStmt)) {
        _exchange = exchange;
        _pageNumber = pageNumber;
        _pageSize = pageSize;
        _minRank = (pageNumber - 1) * pageSize + 1; // Example: pageNumber: 2, pageSize: 10. Then _minRank = 11
        _maxRank = pageNumber * pageSize; // Example: pageNumber: 2, pageSize: 10, Then _maxRank = 20
        _curRankNum = 0;
        PagedInstrumentsWithRawDataReportUpdates = PagedInstrumentsWithRawDataReportUpdatesDto.WithPageNumberAndSizeOnly(_pageNumber, _pageSize);
    }

    protected override void ClearResults() {
        _curRankNum = 0;
        PagedInstrumentsWithRawDataReportUpdates = PagedInstrumentsWithRawDataReportUpdatesDto.WithPageNumberAndSizeOnly(_pageNumber, _pageSize);
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() => new NpgsqlParameter[] {
        new("exchange", NpgsqlTypes.NpgsqlDbType.Text) { Value = _exchange },
        new("rank_min", NpgsqlTypes.NpgsqlDbType.Integer) { Value = _minRank },
        new("rank_max", NpgsqlTypes.NpgsqlDbType.Integer) { Value = _maxRank }
    };

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentIdIndex != -1)
            return;

        _rankNumIndex = reader.GetOrdinal("rank_num");
        _instrumentSymbolIndex = reader.GetOrdinal("instrument_symbol");
        _instrumentNameIndex = reader.GetOrdinal("instrument_name");
        _companySymbolIndex = reader.GetOrdinal("company_symbol");
        _companyNameIndex = reader.GetOrdinal("company_name");
        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _instrumentReportIdIndex = reader.GetOrdinal("instrument_report_id");
        _reportTypeIndex = reader.GetOrdinal("report_type");
        _reportPeriodTypeIndex = reader.GetOrdinal("report_period_type");
        _reportJsonIndex = reader.GetOrdinal("report_json");
        _reportDateIndex = reader.GetOrdinal("report_date");
        _createdDateIndex = reader.GetOrdinal("created_date");
        _isCurrentIndex = reader.GetOrdinal("is_current");
        _checkManuallyIndex = reader.GetOrdinal("check_manually");
        _ignoreReportIndex = reader.GetOrdinal("ignore_report");
        _maxRankNumIndex = reader.GetOrdinal("max_rank_num");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        if (!PagedInstrumentsWithRawDataReportUpdates.IsValid)
            EnsurePagedInstrumentReports(reader);

        var rankNum = reader.GetInt32(_rankNumIndex);
        if (rankNum != _curRankNum)
            AppendNewInstrumentWithRawDataOverrides(reader, rankNum);

        AppendRawDataReport(reader);

        return true;
    }

    private void EnsurePagedInstrumentReports(NpgsqlDataReader reader) =>
        PagedInstrumentsWithRawDataReportUpdates = PagedInstrumentsWithRawDataReportUpdates with {
            TotalInstruments = reader.GetInt32(_maxRankNumIndex),
            InstrumentsWithUpdates = []
        };

    private void AppendNewInstrumentWithRawDataOverrides(NpgsqlDataReader reader, int rankNum) {
        _curRankNum = rankNum;

        DateTimeOffset curReportDate_ = reader.GetDateTime(_reportDateIndex);
        var curReportDate = new DateOnly(curReportDate_.Year, curReportDate_.Month, curReportDate_.Day);

        PagedInstrumentsWithRawDataReportUpdates.InstrumentsWithUpdates.Add(
            new InstrumentWithUpdatedRawDataDto(
            InstrumentId: reader.GetInt64(_instrumentIdIndex),
            Exchange: _exchange,
            InstrumentSymbol: reader.GetString(_instrumentSymbolIndex),
            InstrumentName: reader.GetString(_instrumentNameIndex),
            CompanySymbol: reader.GetString(_companySymbolIndex),
            CompanyName: reader.GetString(_companyNameIndex),
            ReportType: reader.GetInt32(_reportTypeIndex),
            ReportPeriodType: reader.GetInt32(_reportPeriodTypeIndex),
            ReportDate: curReportDate,
            RawReportAndUpdates: []
        ));
    }

    private void AppendRawDataReport(NpgsqlDataReader reader) =>
        PagedInstrumentsWithRawDataReportUpdates.InstrumentsWithUpdates[^1].RawReportAndUpdates.Add(
            new InstrumentWithUpdatedRawDataItemDto(
                InstrumentReportId: reader.GetInt64(_instrumentReportIdIndex),
                CreatedDate: reader.GetDateTime(_createdDateIndex),
                IsCurrent: reader.GetBoolean(_isCurrentIndex),
                CheckManually: reader.GetBoolean(_checkManuallyIndex),
                IgnoreReport: reader.GetBoolean(_ignoreReportIndex),
                SerializedReport: reader.GetString(_reportJsonIndex)
        ));
}
