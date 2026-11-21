using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;

namespace tsx_ops.Snapshots;

internal enum SnapshotSourceKind {
    /// <summary>Recompute each report from raw data with the current code, as the Aggregator would.</summary>
    Raw,

    /// <summary>Read the reports the Aggregator stored in processed_instrument_reports.</summary>
    Processed,
}

/// <summary>Builds snapshot rows from loaded data. No database access, so it can be unit tested.</summary>
internal static class SnapshotBuilder {
    public const string StatusNoProcessedReport = "no-processed-report";

    public static Snapshot FromRaw(
        IEnumerable<SnapshotInstrument> instruments,
        IReadOnlyDictionary<long, List<CurrentInstrumentRawDataReportDto>> rawReports) {

        return Build(instruments, instrument => {
            // Same steps as Aggregator.ProcessCompanyDataChangedEvent. Its per-company warnings (missing
            // properties) are expected noise here, so they are discarded.
            var builder = new CompanyReportBuilder(
                instrument.InstrumentSymbol, instrument.InstrumentName, instrument.Exchange, instrument.NumShares, NullLogger.Instance);
            if (rawReports.TryGetValue(instrument.InstrumentId, out List<CurrentInstrumentRawDataReportDto>? reports)) {
                foreach (CurrentInstrumentRawDataReportDto report in reports)
                    _ = builder.AddRawReport(report);
            }
            return JsonSerializer.Serialize(builder.Build());
        });
    }

    public static Snapshot FromProcessed(
        IEnumerable<SnapshotInstrument> instruments,
        IReadOnlyDictionary<long, string> processedReports) {

        return Build(instruments, instrument =>
            processedReports.TryGetValue(instrument.InstrumentId, out string? json) ? json : null);
    }

    private static Snapshot Build(IEnumerable<SnapshotInstrument> instruments, Func<SnapshotInstrument, string?> getSerializedReport) {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (SnapshotInstrument instrument in instruments
                     .OrderBy(i => i.InstrumentSymbol, StringComparer.Ordinal)
                     .ThenBy(i => i.InstrumentId)) {
            rows.Add(BuildRow(instrument, getSerializedReport));
        }

        return new Snapshot(SnapshotRowBuilder.Columns, rows);
    }

    private static Dictionary<string, string> BuildRow(SnapshotInstrument instrument, Func<SnapshotInstrument, string?> getSerializedReport) {
        try {
            string? json = getSerializedReport(instrument);
            if (json is null)
                return SnapshotRowBuilder.BuildUnreadable(instrument, StatusNoProcessedReport);

            // Same parsing as the API path; an exception here is what makes the API drop the company.
            return ProcessedReportFields.TryParse(json, out ProcessedReportFields? fields)
                ? SnapshotRowBuilder.Build(instrument, fields)
                : SnapshotRowBuilder.BuildUnreadable(instrument, SnapshotRowBuilder.StatusInvalid);
        } catch (Exception ex) when (ex is OverflowException or JsonException or FormatException) {
            return SnapshotRowBuilder.BuildUnreadable(instrument, "error:" + ex.GetType().Name);
        }
    }
}
