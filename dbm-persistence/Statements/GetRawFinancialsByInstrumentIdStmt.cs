using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetRawFinancialsByInstrumentIdStmt : QueryDbStmtBase {
    private const string sql = "SELECT ir.instrument_report_id, ir.instrument_id, ir.report_type, ir.report_period_type, ir.report_json, ir.report_date,"
        + " ir.created_date, ir.obsoleted_date, ir.is_current"
        + " FROM instrument_reports ir"
        + " WHERE ir.instrument_id = @instrument_id"
        + " AND ir.is_current = true";

    // Inputs
    private readonly long _instrumentId;

    // Results
    private readonly List<CurrentInstrumentReportDto> _instrumentReportDtoList; // Array of type InstrumentReportDto

    public GetRawFinancialsByInstrumentIdStmt(long instrumentId) : base(sql, nameof(GetRawFinancialsByInstrumentIdStmt)) {
        _instrumentId = instrumentId;
        _instrumentReportDtoList = new();
    }

    public IReadOnlyList<CurrentInstrumentReportDto> InstrumentReports => _instrumentReportDtoList;

    protected override void ClearResults() {
        _instrumentReportDtoList.Clear();
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] { new NpgsqlParameter<long>("instrument_id", _instrumentId) };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        DateTimeOffset reportDate_ = reader.GetDateTime(5);
        var reportDate = new DateOnly(reportDate_.Year, reportDate_.Month, reportDate_.Day);
        var i = new CurrentInstrumentReportDto(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reportDate);
        _instrumentReportDtoList.Add(i);
        return true;
    }
}
