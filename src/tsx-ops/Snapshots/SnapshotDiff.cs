using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using tsx_aggregator.models;

namespace tsx_ops.Snapshots;

internal sealed record DiffRow(string InstrumentId, string Symbol, int? OldScore, int? NewScore, IReadOnlyList<string> FlippedChecks) {
    public int Delta => (NewScore ?? 0) - (OldScore ?? 0);
}

internal sealed record ThresholdChange(int Threshold, IReadOnlyList<DiffRow> Entered, IReadOnlyList<DiffRow> Left);

/// <summary>
/// The differences between two snapshots, matched by instrument_id.
/// Instruments without a score (status not "ok") count as being in no score group.
/// </summary>
internal sealed class SnapshotDiffResult {
    public required int RowsA { get; init; }
    public required int RowsB { get; init; }
    public required IReadOnlyList<string> OnlyInA { get; init; }
    public required IReadOnlyList<string> OnlyInB { get; init; }
    public required IReadOnlyList<string> ColumnsOnlyInA { get; init; }
    public required IReadOnlyList<string> ColumnsOnlyInB { get; init; }
    public required int CommonCount { get; init; }
    public required IReadOnlyList<DiffRow> ScoreChanges { get; init; }
    public required IReadOnlyDictionary<int, int> DeltaHistogram { get; init; }
    public required IReadOnlyList<ThresholdChange> Thresholds { get; init; }

    /// <summary>Per check column, counts keyed by "old→new" (e.g. "P→F").</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CheckFlips { get; init; }

    /// <summary>Per non-check column present in both snapshots, how many instruments changed value.</summary>
    public required IReadOnlyDictionary<string, int> ColumnChanges { get; init; }

    public required IReadOnlyList<(string Symbol, string OldStatus, string NewStatus)> StatusChanges { get; init; }

    public bool HasNoChanges =>
        OnlyInA.Count == 0 && OnlyInB.Count == 0
        && ColumnsOnlyInA.Count == 0 && ColumnsOnlyInB.Count == 0
        && ColumnChanges.Values.All(n => n == 0)
        && CheckFlips.Values.All(f => f.Count == 0);
}

internal static class SnapshotDiff {
    public static readonly IReadOnlyList<int> DefaultThresholds = [ScoringOptions.TopScoreCutoff, 15, SnapshotRowBuilder.NumChecks];

    private static readonly HashSet<string> IdentityColumns = new(StringComparer.Ordinal) {
        Snapshot.InstrumentIdColumn, Snapshot.SymbolColumn, "company_name",
    };

    public static SnapshotDiffResult Compute(Snapshot a, Snapshot b, IReadOnlyList<int>? thresholds = null) {
        thresholds ??= DefaultThresholds;

        var columnsA = new HashSet<string>(a.Columns, StringComparer.Ordinal);
        var columnsB = new HashSet<string>(b.Columns, StringComparer.Ordinal);
        List<string> sharedColumns = a.Columns.Where(columnsB.Contains).ToList();
        List<string> checkColumns = sharedColumns.Where(IsCheckColumn).ToList();
        List<string> valueColumns = sharedColumns.Where(c => !IsCheckColumn(c) && !IdentityColumns.Contains(c)).ToList();

        var checkFlips = checkColumns.ToDictionary(c => c, _ => new SortedDictionary<string, int>(StringComparer.Ordinal), StringComparer.Ordinal);
        var columnChanges = valueColumns.ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
        var scoreChanges = new List<DiffRow>();
        var allRows = new List<DiffRow>();
        var statusChanges = new List<(string, string, string)>();

        foreach ((string id, IReadOnlyDictionary<string, string> rowA) in a.RowsById) {
            if (!b.RowsById.TryGetValue(id, out IReadOnlyDictionary<string, string>? rowB))
                continue;

            var flipped = new List<string>();
            foreach (string column in checkColumns) {
                string oldValue = rowA[column];
                string newValue = rowB[column];
                if (oldValue == newValue)
                    continue;
                string key = $"{Display(oldValue)}→{Display(newValue)}";
                checkFlips[column][key] = checkFlips[column].GetValueOrDefault(key) + 1;
                flipped.Add($"{column} {key}");
            }

            foreach (string column in valueColumns) {
                if (rowA[column] != rowB[column])
                    columnChanges[column]++;
            }

            string oldStatus = rowA.GetValueOrDefault(Snapshot.StatusColumn, string.Empty);
            string newStatus = rowB.GetValueOrDefault(Snapshot.StatusColumn, string.Empty);
            if (oldStatus != newStatus)
                statusChanges.Add((rowB[Snapshot.SymbolColumn], oldStatus, newStatus));

            var diffRow = new DiffRow(id, rowB[Snapshot.SymbolColumn], ParseScore(rowA), ParseScore(rowB), flipped);
            allRows.Add(diffRow);
            if (diffRow.OldScore != diffRow.NewScore)
                scoreChanges.Add(diffRow);
        }

        var histogram = new SortedDictionary<int, int>();
        foreach (DiffRow row in scoreChanges.Where(r => r.OldScore is not null && r.NewScore is not null))
            histogram[row.Delta] = histogram.GetValueOrDefault(row.Delta) + 1;

        List<ThresholdChange> thresholdChanges = thresholds.Select(t => new ThresholdChange(
            t,
            allRows.Where(r => !InGroup(r.OldScore, t) && InGroup(r.NewScore, t)).OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList(),
            allRows.Where(r => InGroup(r.OldScore, t) && !InGroup(r.NewScore, t)).OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList()))
            .ToList();

        return new SnapshotDiffResult {
            RowsA = a.Rows.Count,
            RowsB = b.Rows.Count,
            OnlyInA = a.RowsById.Keys.Where(id => !b.RowsById.ContainsKey(id)).Select(id => a.RowsById[id][Snapshot.SymbolColumn]).Order(StringComparer.Ordinal).ToList(),
            OnlyInB = b.RowsById.Keys.Where(id => !a.RowsById.ContainsKey(id)).Select(id => b.RowsById[id][Snapshot.SymbolColumn]).Order(StringComparer.Ordinal).ToList(),
            ColumnsOnlyInA = a.Columns.Where(c => !columnsB.Contains(c)).ToList(),
            ColumnsOnlyInB = b.Columns.Where(c => !columnsA.Contains(c)).ToList(),
            CommonCount = allRows.Count,
            ScoreChanges = scoreChanges
                .OrderByDescending(r => Math.Abs(r.Delta))
                .ThenBy(r => r.Symbol, StringComparer.Ordinal)
                .ToList(),
            DeltaHistogram = histogram,
            Thresholds = thresholdChanges,
            CheckFlips = checkFlips.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value, StringComparer.Ordinal),
            ColumnChanges = columnChanges,
            StatusChanges = statusChanges.OrderBy(s => s.Item1, StringComparer.Ordinal).ToList(),
        };
    }

    public static string RenderMarkdown(SnapshotDiffResult diff, string nameA, string nameB, int topMovers) {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);

        Line("# Score Snapshot Diff");
        Line();
        Line($"- A: `{nameA}` ({Inv(diff.RowsA)} rows)");
        Line($"- B: `{nameB}` ({Inv(diff.RowsB)} rows)");
        Line($"- Instruments in both: {Inv(diff.CommonCount)}");
        if (diff.OnlyInA.Count > 0)
            Line($"- Only in A ({Inv(diff.OnlyInA.Count)}): {string.Join(", ", diff.OnlyInA)}");
        if (diff.OnlyInB.Count > 0)
            Line($"- Only in B ({Inv(diff.OnlyInB.Count)}): {string.Join(", ", diff.OnlyInB)}");
        if (diff.ColumnsOnlyInA.Count > 0)
            Line($"- Columns only in A: {string.Join(", ", diff.ColumnsOnlyInA)}");
        if (diff.ColumnsOnlyInB.Count > 0)
            Line($"- Columns only in B: {string.Join(", ", diff.ColumnsOnlyInB)}");
        Line();

        if (diff.HasNoChanges) {
            Line("**No differences.**");
            return sb.ToString();
        }

        Line("## Scores");
        Line();
        Line($"{Inv(diff.ScoreChanges.Count)} of {Inv(diff.CommonCount)} instruments changed score.");
        if (diff.DeltaHistogram.Count > 0) {
            Line();
            Line("| Change | Instruments |");
            Line("|---|---|");
            foreach ((int delta, int count) in diff.DeltaHistogram)
                Line($"| {(delta > 0 ? "+" : string.Empty)}{Inv(delta)} | {Inv(count)} |");
        }
        Line();

        Line("## Score Groups");
        Line();
        foreach (ThresholdChange t in diff.Thresholds) {
            string label = t.Threshold >= SnapshotRowBuilder.NumChecks
                ? $"{Inv(t.Threshold)}/{Inv(SnapshotRowBuilder.NumChecks)}"
                : $"{Inv(t.Threshold)}+";
            Line($"- **{label}**: {Inv(t.Entered.Count)} entered, {Inv(t.Left.Count)} left");
            if (t.Entered.Count > 0)
                Line($"  - Entered: {string.Join(", ", t.Entered.Select(FormatMove))}");
            if (t.Left.Count > 0)
                Line($"  - Left: {string.Join(", ", t.Left.Select(FormatMove))}");
        }
        Line();

        Line("## Check Flips");
        Line();
        Line("| Check | Changes |");
        Line("|---|---|");
        foreach ((string column, IReadOnlyDictionary<string, int> flips) in diff.CheckFlips) {
            string text = flips.Count == 0
                ? "none"
                : string.Join(", ", flips.Select(f => $"{f.Key} {Inv(f.Value)}"));
            Line($"| {column} | {text} |");
        }
        Line();

        Line("## Changed Values");
        Line();
        Line("| Column | Instruments changed |");
        Line("|---|---|");
        foreach ((string column, int count) in diff.ColumnChanges)
            Line($"| {column} | {Inv(count)} |");
        Line();

        if (diff.StatusChanges.Count > 0) {
            Line("## Status Changes");
            Line();
            foreach ((string symbol, string oldStatus, string newStatus) in diff.StatusChanges)
                Line($"- {symbol}: {oldStatus} → {newStatus}");
            Line();
        }

        if (diff.ScoreChanges.Count > 0 && topMovers > 0) {
            Line($"## Top Movers");
            Line();
            Line("| Symbol | Score | Checks flipped |");
            Line("|---|---|---|");
            foreach (DiffRow row in diff.ScoreChanges.Take(topMovers))
                Line($"| {row.Symbol} | {FormatScore(row.OldScore)} → {FormatScore(row.NewScore)} | {string.Join(", ", row.FlippedChecks)} |");
        }

        return sb.ToString();
    }

    private static bool IsCheckColumn(string column) =>
        column.Length == 3 && column[0] == 'c' && char.IsAsciiDigit(column[1]) && char.IsAsciiDigit(column[2]);

    private static bool InGroup(int? score, int threshold) => score is not null && score.Value >= threshold;

    private static int? ParseScore(IReadOnlyDictionary<string, string> row) =>
        row.TryGetValue(Snapshot.ScoreColumn, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int score)
            ? score
            : null;

    private static string Display(string checkValue) => checkValue.Length == 0 ? "-" : checkValue;

    private static string FormatScore(int? score) => score?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatMove(DiffRow row) => $"{row.Symbol} ({FormatScore(row.OldScore)}→{FormatScore(row.NewScore)})";
}
