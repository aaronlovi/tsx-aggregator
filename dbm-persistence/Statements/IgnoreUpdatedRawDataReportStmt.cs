using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using tsx_aggregator.models;

namespace dbm_persistence;

internal sealed class IgnoreUpdatedRawDataReportStmt : NonQueryDbStmtBase {
    internal const string sql = "UPDATE instrument_reports SET ignore_report = true"
        + "WHERE instrument_report_id = @instrument_report_id";

    // Inputs
    private ulong _instrumentReportId;

    public IgnoreUpdatedRawDataReportStmt(ulong instrumentReportId)
        : base(sql, nameof(IgnoreUpdatedRawDataReportStmt))
        => _instrumentReportId = instrumentReportId;

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters()
        => new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_report_id", (long)_instrumentReportId)
        };
}
