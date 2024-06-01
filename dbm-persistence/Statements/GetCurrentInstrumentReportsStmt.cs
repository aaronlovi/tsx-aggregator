using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetCurrentInstrumentReportsStmt : QueryDbStmtBase {
    private const string sql = "SELECT instrument_report_id, report_type, report_period_type, report_json, report_date"
        + " FROM instrument_reports"
        + " WHERE is_current = true"
        + " AND instrument_id = @instrumentId";

    private readonly long _instrumentId;
    private readonly List<CurrentInstrumentReportDto> _instrumentReports;

    public GetCurrentInstrumentReportsStmt(long instrumentId)
        : base(sql, nameof(GetCurrentInstrumentReportsStmt)) 
    {
        _instrumentId = instrumentId;
        _instrumentReports = new();
    }

    public IReadOnlyList<CurrentInstrumentReportDto> InstrumentReports => _instrumentReports;

    protected override void ClearResults() {
        _instrumentReports.Clear();
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new List<NpgsqlParameter> {
            new NpgsqlParameter<long>("instrumentId", _instrumentId)
        };
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        DateTimeOffset reportDate_ = reader.GetDateTime(4);
        DateOnly reportDate = new DateOnly(reportDate_.Year, reportDate_.Month, reportDate_.Day);
        var i = new CurrentInstrumentReportDto(
            reader.GetInt64(0),
            _instrumentId,
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reportDate);
        _instrumentReports.Add(i);
        return true;
    }
}
