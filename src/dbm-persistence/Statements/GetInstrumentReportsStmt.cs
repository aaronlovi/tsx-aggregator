using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class GetInstrumentReportsStmt : QueryDbStmtBase {
    private const string sql = "SELECT instrument_report_id, instrument_id, report_type, report_period_type, report_json,"
        + " report_date, created_date, obsoleted_date"
        + " FROM instrument_reports"
        + " WHERE instrument_id = @instrumentId"
        + " AND is_current = true";

    private readonly long _instrumentId;
    private readonly List<InstrumentRawDataReportDto> _instrumentReports;

    private static int _instrumentReportIdIndex = -1;
    private static int _instrumentIdIndex = -1;
    private static int _reportTypeIndex = -1;
    private static int _reportPeriodTypeIndex = -1;
    private static int _reportJsonIndex = -1;
    private static int _reportDateIndex = -1;
    private static int _createdDateIndex = -1;
    private static int _obsoletedDateIndex = -1;

    public GetInstrumentReportsStmt(long instrumentId)
        : base(sql, nameof(GetInstrumentReportsStmt)) {
        _instrumentId = instrumentId;
        _instrumentReports = [];
    }

    public IReadOnlyList<InstrumentRawDataReportDto> InstrumentReports => _instrumentReports;

    protected override void ClearResults() => _instrumentReports.Clear();

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() =>
        new List<NpgsqlParameter> { new NpgsqlParameter<long>("instrumentId", _instrumentId) };

    protected override void BeforeRowProcessing(NpgsqlDataReader reader) {
        base.BeforeRowProcessing(reader);

        if (_instrumentReportIdIndex != -1)
            return; // Column indices are already cached. Nothing to do.

        _instrumentReportIdIndex = reader.GetOrdinal("instrument_report_id");
        _instrumentIdIndex = reader.GetOrdinal("instrument_id");
        _reportTypeIndex = reader.GetOrdinal("report_type");
        _reportPeriodTypeIndex = reader.GetOrdinal("report_period_type");
        _reportJsonIndex = reader.GetOrdinal("report_json");
        _reportDateIndex = reader.GetOrdinal("report_date");
        _createdDateIndex = reader.GetOrdinal("created_date");
        _obsoletedDateIndex = reader.GetOrdinal("obsoleted_date");
    }

    protected override bool ProcessCurrentRow(NpgsqlDataReader reader) {
        DateTimeOffset reportDate_ = reader.GetDateTime(_reportDateIndex);
        var reportDate = new DateOnly(reportDate_.Year, reportDate_.Month, reportDate_.Day);

        DateTimeOffset createdDate = reader.GetDateTime(_createdDateIndex);
        DateTimeOffset? obsoletedDate = reader.GetFieldValue<DateTime?>(_obsoletedDateIndex);

        var report = new InstrumentRawDataReportDto(
            reader.GetInt64(_instrumentReportIdIndex),
            reader.GetInt64(_instrumentIdIndex),
            reader.GetInt32(_reportTypeIndex),
            reader.GetInt32(_reportPeriodTypeIndex),
            reader.GetString(_reportJsonIndex),
            reportDate,
            createdDate,
            obsoletedDate,
            IsCurrent: true);
        _instrumentReports.Add(report);

        return true;
    }
}
