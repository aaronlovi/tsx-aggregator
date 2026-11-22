using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using static tsx_aggregator.shared.Constants;

namespace tsx_ops.Events;

/// <summary>Unprocessed instrument events of one type: how many the Aggregator can pick up, and how many it can't.</summary>
internal sealed record EventQueueRow(int EventType, long Pending, long Stuck, DateTimeOffset? OldestPending);

/// <summary>The Aggregator's event queue, as <c>tsx-ops events</c> shows it.</summary>
internal static class EventQueue {
    // Same rule as the Aggregator's GetNextInstrumentEventStmt (B14, decision 25): only RawDataChanged (4) needs a
    // listed instrument and a current price row. Anything it can't pick up is stuck.
    private const string Sql = """
        SELECT ie.event_type,
               COUNT(*) FILTER (WHERE eligible) AS pending,
               COUNT(*) FILTER (WHERE NOT eligible) AS stuck,
               MIN(ie.event_date) FILTER (WHERE eligible) AS oldest_pending
        FROM (
            SELECT ie.event_type, ie.event_date,
                   (ie.event_type <> 4 OR (i.obsoleted_date IS NULL AND EXISTS (
                       SELECT 1 FROM instrument_prices ip
                       WHERE ip.instrument_id = ie.instrument_id AND ip.obsoleted_date IS NULL))) AS eligible
            FROM instrument_events ie
            JOIN instruments i ON i.instrument_id = ie.instrument_id
            WHERE ie.is_processed = FALSE
        ) ie
        GROUP BY ie.event_type
        ORDER BY ie.event_type
        """;

    public static async Task<List<EventQueueRow>> LoadAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct) {
        var rows = new List<EventQueueRow>();
        await using var cmd = new NpgsqlCommand(Sql, conn, tx);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            rows.Add(new EventQueueRow(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetDateTime(3).ToUniversalTime(), TimeSpan.Zero)));
        }
        return rows;
    }

    public static string Format(IReadOnlyList<EventQueueRow> rows) {
        var sb = new StringBuilder();
        const string TypeHeader = "Event type";
        int width = Math.Max(TypeHeader.Length, rows.Select(r => TypeName(r.EventType).Length).DefaultIfEmpty(0).Max());
        _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{TypeHeader.PadRight(width)}  {"Pending",7}  {"Stuck",5}  Oldest pending (UTC)");
        foreach (EventQueueRow row in rows) {
            string oldest = row.OldestPending?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
            _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{TypeName(row.EventType).PadRight(width)}  {row.Pending,7}  {row.Stuck,5}  {oldest}");
        }

        long pending = rows.Sum(r => r.Pending);
        long stuck = rows.Sum(r => r.Stuck);
        _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{"Total".PadRight(width)}  {pending,7}  {stuck,5}");
        if (stuck > 0)
            _ = sb.AppendLine("Stuck: RawDataChanged for a delisted instrument or one without a current price row; the Aggregator skips these.");
        return sb.ToString();
    }

    private static string TypeName(int eventType) =>
        Enum.IsDefined(typeof(CompanyEventTypes), eventType)
            ? ((CompanyEventTypes)eventType).ToString()
            : eventType.ToString(CultureInfo.InvariantCulture);
}
