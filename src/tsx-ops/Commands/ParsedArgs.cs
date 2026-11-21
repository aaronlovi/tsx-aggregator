using System;
using System.Collections.Generic;
using System.Globalization;

namespace tsx_ops.Commands;

/// <summary>
/// Parses "--name value" options, "--flag" switches and positional arguments. Unknown options are usage errors.
/// </summary>
internal sealed class ParsedArgs {
    /// <summary>Accepted by every command that touches the database.</summary>
    public const string ConnectionOption = "--connection";

    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _flags;

    private ParsedArgs(Dictionary<string, string> values, HashSet<string> flags, List<string> positionals) {
        _values = values;
        _flags = flags;
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public static ParsedArgs Parse(string[] args, IReadOnlyCollection<string> valueOptions, IReadOnlyCollection<string> flagOptions) {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var positionals = new List<string>();
        var valueSet = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        var flagSet = new HashSet<string>(flagOptions, StringComparer.Ordinal);

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                positionals.Add(arg);
                continue;
            }

            if (flagSet.Contains(arg)) {
                _ = flags.Add(arg);
            } else if (valueSet.Contains(arg)) {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new UsageException($"Option {arg} needs a value");
                values[arg] = args[++i];
            } else {
                throw new UsageException($"Unknown option {arg}");
            }
        }

        return new ParsedArgs(values, flags, positionals);
    }

    public string? Get(string option) => _values.TryGetValue(option, out string? v) ? v : null;

    public bool Has(string flag) => _flags.Contains(flag);

    public int GetInt(string option, int defaultValue) {
        string? value = Get(option);
        if (value is null)
            return defaultValue;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < 0)
            throw new UsageException($"Option {option} needs a non-negative whole number, got '{value}'");
        return result;
    }
}
