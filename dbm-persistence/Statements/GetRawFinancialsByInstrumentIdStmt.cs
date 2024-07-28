using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetRawFinancialsByInstrumentIdStmt : QueryDbStmtBase {
    private const string sql = "SELECT ir.instrument_report_id, ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_json, ir.report_date,"
        + " ir.created_date, ir.obsoleted_date, ir.is_current, ir.check_manually"
        + " FROM instrument_reports ir"
        + " WHERE ir.instrument_id = @instrument_id"
        + " AND ir.is_current = true"
        + " AND ir.check_manually = false";

    // Inputs
    private readonly long _instrumentId;

    private static int _instrumentReportIdIndex = -1;
    private static int _instrumentIdIndex = -1;
    private static int _reportTypeIndex = -1;
    private static int _reportPeriodTypeIndex = -1;
    private static int _reportJsonIndex = -1;
    private static int _reportDateIndex = -1;
    private static int _createdDateIndex = -1;
    private static int _obsoletedDateIndex = -1;
    private static int _isCurrentIndex = -1;
    private static int _checkManuallyIndex = -1;

    // Results
    private readonly List<CurrentInstrumentReportDto> _instrumentReportDtoList; // Array of type InstrumentReportDto

    public GetRawFinancialsByInstrumentIdStmt(long instrumentId) : base(sql, nameof(GetRawFinancialsByInstrumentIdStmt)) {
        _instrumentId = instrumentId;
        _instrumentReportDtoList = new();
    }

    public IReadOnlyList<CurrentInstrumentReportDto> InstrumentReports => _instrumentReportDtoList;

    protected override void ClearResults() => _instrumentReportDtoList.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        new NpgsqlParameter[] { new NpgsqlParameter<long>("instrument_id", _instrumentId) };

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentReportIdIndex != -1)
            return;

        _instrumentReportIdIndex = reader.GetOrdinal("instrument_report_id");
        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _reportTypeIndex = reader.GetOrdinal("report_type");
        _reportPeriodTypeIndex = reader.GetOrdinal("report_period_type");
        _reportJsonIndex = reader.GetOrdinal("report_json");
        _reportDateIndex = reader.GetOrdinal("report_date");
        _createdDateIndex = reader.GetOrdinal("created_date");
        _obsoletedDateIndex = reader.GetOrdinal("obsoleted_date");
        _isCurrentIndex = reader.GetOrdinal("is_current");
        _checkManuallyIndex = reader.GetOrdinal("check_manually");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        DateTimeOffset reportDate_ = reader.GetDateTime(_reportDateIndex);
        var reportDate = new DateOnly(reportDate_.Year, reportDate_.Month, reportDate_.Day);
        var i = new CurrentInstrumentReportDto(
            reader.GetInt64(_instrumentReportIdIndex),
            reader.GetInt64(_instrumentIdIndex),
            reader.GetInt32(_reportTypeIndex),
            reader.GetInt32(_reportPeriodTypeIndex),
            reader.GetString(_reportJsonIndex),
            reportDate,
            reader.GetBoolean(_checkManuallyIndex));
        _instrumentReportDtoList.Add(i);
        return true;
    }
}
