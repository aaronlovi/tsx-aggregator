using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace tsx_ops.Snapshots;

/// <summary>
/// One snapshot: a header of column names and one row per instrument, keyed by instrument_id.
/// Rows are kept as strings so a diff works across snapshots taken with different code versions,
/// including ones where columns were added or removed.
/// </summary>
internal sealed class Snapshot {
    public const string InstrumentIdColumn = "instrument_id";
    public const string SymbolColumn = "instrument_symbol";
    public const string StatusColumn = "status";
    public const string ScoreColumn = "score";

    public Snapshot(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyDictionary<string, string>> rows) {
        Columns = columns;
        Rows = rows;
        RowsById = rows.ToDictionary(r => r[InstrumentIdColumn]);
    }

    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RowsById { get; }

    public static Snapshot Read(TextReader reader) {
        List<string[]> lines = Csv.ReadAll(reader);
        if (lines.Count == 0)
            throw new InvalidDataException("Snapshot file is empty");

        string[] header = lines[0];
        if (!header.Contains(InstrumentIdColumn))
            throw new InvalidDataException($"Snapshot file has no '{InstrumentIdColumn}' column");

        var rows = new List<IReadOnlyDictionary<string, string>>(lines.Count - 1);
        for (int i = 1; i < lines.Count; i++) {
            string[] line = lines[i];
            if (line.Length != header.Length)
                throw new InvalidDataException($"Snapshot line {i + 1} has {line.Length} fields, expected {header.Length}");

            var row = new Dictionary<string, string>(header.Length, StringComparer.Ordinal);
            for (int j = 0; j < header.Length; j++)
                row[header[j]] = line[j];
            rows.Add(row);
        }

        return new Snapshot(header, rows);
    }

    public static Snapshot ReadFile(string path) {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    public void Write(TextWriter writer) {
        writer.NewLine = "\n";
        writer.WriteLine(Csv.FormatLine(Columns));
        foreach (IReadOnlyDictionary<string, string> row in Rows)
            writer.WriteLine(Csv.FormatLine(Columns.Select(c => row.TryGetValue(c, out string? v) ? v : string.Empty)));
    }
}
