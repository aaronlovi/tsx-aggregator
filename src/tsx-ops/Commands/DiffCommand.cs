using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using tsx_ops.Snapshots;

namespace tsx_ops.Commands;

internal sealed class DiffCommand : ICommand {
    private const string TopOption = "--top";
    private const int DefaultTopMovers = 20;

    public string Name => "diff";

    public string Description => "Compare two snapshot CSVs and print a Markdown summary (no database access)";

    public string Usage => """
        Usage: tsx-ops diff <snapshot-a.csv> <snapshot-b.csv> [--top <n>]

        Compares two snapshots by instrument and prints a Markdown summary to stdout: score changes,
        companies entering or leaving 14+, 15+ and 16/16, per-check flips, changed values, and the top movers.

          --top <n>  Number of top movers to list (default: 20)
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [TopOption], []);
        if (parsed.Positionals.Count != 2)
            throw new UsageException("diff needs exactly two snapshot files");

        string pathA = parsed.Positionals[0];
        string pathB = parsed.Positionals[1];
        foreach (string path in parsed.Positionals) {
            if (!File.Exists(path))
                throw new UsageException($"Snapshot file not found: {path}");
        }

        Snapshot a = Snapshot.ReadFile(pathA);
        Snapshot b = Snapshot.ReadFile(pathB);
        SnapshotDiffResult diff = SnapshotDiff.Compute(a, b);

        string report = SnapshotDiff.RenderMarkdown(diff, Path.GetFileName(pathA), Path.GetFileName(pathB), parsed.GetInt(TopOption, DefaultTopMovers));
        await context.Output.WriteAsync(report.AsMemory(), ct);
        return ExitCodes.Ok;
    }
}
