using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace tsx_ops.Snapshots;

/// <summary>
/// Read-only queries for building a snapshot. Run inside OpsDb.RunReadOnlyAsync so all reads see one point in time.
/// </summary>
internal static class SnapshotSource {
    /// <summary>
    /// Instruments the API would list: listed, on the TSX, with a current price row (as the Aggregator's
    /// GetNextInstrumentEventStmt requires) and at least one current annual cash-flow report (as the API's
    /// GetProcessedStockDataByExchangeStmt requires via its join). The count is the API's NumAnnualCashFlowReports.
    /// </summary>
    private const string InstrumentsSql = """
        SELECT i.instrument_id, i.exchange, i.company_symbol, i.company_name, i.instrument_symbol, i.instrument_name,
               ip.price_per_share, ip.num_shares, cf.num_annual_cash_flow_reports, i.sector, i.industry
        FROM instruments i
        JOIN instrument_prices ip ON ip.instrument_id = i.instrument_id AND ip.obsoleted_date IS NULL
        JOIN (SELECT instrument_id, COUNT(*)::int AS num_annual_cash_flow_reports
              FROM instrument_reports
              WHERE is_current = TRUE AND report_type = @cashFlow AND report_period_type = @annual
              GROUP BY instrument_id) cf ON cf.instrument_id = i.instrument_id
        WHERE i.obsoleted_date IS NULL
          AND i.exchange = @exchange
        """;

    /// <summary>Same selection as GetCurrentInstrumentReportsStmt, for all instruments in one query.</summary>
    private const string CurrentRawReportsSql = """
        SELECT instrument_report_id, instrument_id, report_type, report_period_type, report_json, report_date
        FROM instrument_reports
        WHERE is_current = TRUE
        """;

    /// <summary>The current processed report per instrument, as the API reads it.</summary>
    private const string ProcessedReportsSql = """
        SELECT DISTINCT ON (instrument_id) instrument_id, report_json
        FROM processed_instrument_reports
        WHERE obsoleted_date IS NULL
        ORDER BY instrument_id, created_date DESC
        """;

    public static async Task<List<SnapshotInstrument>> LoadInstrumentsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand(InstrumentsSql, conn, tx);
        _ = cmd.Parameters.AddWithValue("cashFlow", (int)ReportTypes.CashFlow);
        _ = cmd.Parameters.AddWithValue("annual", (int)ReportPeriodTypes.Annual);
        _ = cmd.Parameters.AddWithValue("exchange", TsxExchange);

        var instruments = new List<SnapshotInstrument>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            instruments.Add(new SnapshotInstrument(
                InstrumentId: reader.GetInt64(0),
                Exchange: reader.GetString(1),
                CompanySymbol: reader.GetString(2),
                CompanyName: reader.GetString(3),
                InstrumentSymbol: reader.GetString(4),
                InstrumentName: reader.GetString(5),
                PricePerShare: reader.GetDecimal(6),
                NumShares: reader.GetInt64(7),
                NumAnnualCashFlowReports: reader.GetInt32(8),
                Sector: reader.IsDBNull(9) ? null : reader.GetString(9),
                Industry: reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return instruments;
    }

    public static async Task<Dictionary<long, List<CurrentInstrumentRawDataReportDto>>> LoadCurrentRawReportsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct) {

        await using var cmd = new NpgsqlCommand(CurrentRawReportsSql, conn, tx);
        var reports = new Dictionary<long, List<CurrentInstrumentRawDataReportDto>>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            long instrumentId = reader.GetInt64(1);

            // Same date conversion as GetCurrentInstrumentReportsStmt.
            DateTimeOffset reportDate = reader.GetDateTime(5);
            var report = new CurrentInstrumentRawDataReportDto(
                InstrumentReportId: reader.GetInt64(0),
                InstrumentId: instrumentId,
                ReportType: reader.GetInt32(2),
                ReportPeriodType: reader.GetInt32(3),
                ReportJson: reader.GetString(4),
                ReportDate: new DateOnly(reportDate.Year, reportDate.Month, reportDate.Day));

            if (!reports.TryGetValue(instrumentId, out List<CurrentInstrumentRawDataReportDto>? list)) {
                list = [];
                reports.Add(instrumentId, list);
            }
            list.Add(report);
        }

        return reports;
    }

    public static async Task<Dictionary<long, string>> LoadProcessedReportsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand(ProcessedReportsSql, conn, tx);
        var reports = new Dictionary<long, string>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            reports[reader.GetInt64(0)] = reader.GetString(1);
        return reports;
    }
}
