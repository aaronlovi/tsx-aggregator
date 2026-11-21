using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace tsx_ops.Snapshots;

/// <summary>
/// Minimal RFC 4180 style CSV reading and writing. Fields containing commas, quotes or line breaks are quoted.
/// </summary>
internal static class Csv {
    public static string FormatLine(IEnumerable<string> fields) => string.Join(",", fields.Select(Quote));

    public static List<string[]> ReadAll(TextReader reader) {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool rowHasContent = false;

        int c;
        while ((c = reader.Read()) != -1) {
            char ch = (char)c;
            if (inQuotes) {
                if (ch == '"') {
                    if (reader.Peek() == '"') {
                        _ = field.Append('"');
                        _ = reader.Read();
                    } else {
                        inQuotes = false;
                    }
                } else {
                    _ = field.Append(ch);
                }
                continue;
            }

            switch (ch) {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    _ = field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (rowHasContent || field.Length > 0) {
                        fields.Add(field.ToString());
                        rows.Add([.. fields]);
                    }
                    fields.Clear();
                    _ = field.Clear();
                    rowHasContent = false;
                    break;
                default:
                    _ = field.Append(ch);
                    rowHasContent = true;
                    break;
            }
        }

        if (rowHasContent || field.Length > 0) {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }

    private static string Quote(string field) {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
