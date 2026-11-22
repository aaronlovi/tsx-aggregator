using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_ops.Db;
using tsx_ops.Snapshots;

namespace tsx_ops.Commands;

internal sealed class SnapshotCommand : ICommand {
    private const string SourceOption = "--source";
    private const string LabelOption = "--label";
    private const string OutOption = "--out";

    public string Name => "snapshot";

    public string Description => "Compute every company's score and write it to a CSV (read-only)";

    public string Usage => """
        Usage: tsx-ops snapshot [--source raw|processed] [--label <name>] [--out <file>] [--connection <string>]

        Computes the 15-check score for every listed TSX company with frozen prices from instrument_prices,
        and writes one CSV row per company. All reads run in one read-only, repeatable-read transaction.

          --source raw        Recompute each report from raw data with the current code (default)
          --source processed  Read the reports stored in processed_instrument_reports
          --label <name>      Added to the file name (default: snapshot)
          --out <file>        Output file (default: <repo root>/snapshots/<time>-<git sha>[-dirty]-<label>.csv)
          --connection <str>  Database connection string (default: ConnectionStrings:tsx-scraper from configuration)

        Prints the output path to stdout and a summary to stderr.
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [SourceOption, LabelOption, OutOption, ParsedArgs.ConnectionOption], []);
        if (parsed.Positionals.Count > 0)
            throw new UsageException($"Unexpected argument '{parsed.Positionals[0]}'");

        SnapshotSourceKind source = (parsed.Get(SourceOption) ?? "raw") switch {
            "raw" => SnapshotSourceKind.Raw,
            "processed" => SnapshotSourceKind.Processed,
            string other => throw new UsageException($"--source must be 'raw' or 'processed', got '{other}'"),
        };
        string label = SanitizeLabel(parsed.Get(LabelOption) ?? "snapshot");
        string connectionString = context.GetConnectionString(parsed.Get(ParsedArgs.ConnectionOption));
        ILogger logger = context.CreateLogger<SnapshotCommand>();

        ScoringOptions scoring = context.GetScoringOptions();
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Reading {Source} data in a read-only transaction", source.ToString().ToLowerInvariant());

        Snapshot snapshot = await OpsDb.RunReadOnlyAsync(connectionString, context.StatementTimeoutSeconds, async (conn, tx, token) => {
            List<SnapshotInstrument> instruments = await SnapshotSource.LoadInstrumentsAsync(conn, tx, token);
            if (source == SnapshotSourceKind.Raw) {
                Dictionary<long, List<CurrentInstrumentRawDataReportDto>> raw = await SnapshotSource.LoadCurrentRawReportsAsync(conn, tx, token);
                return SnapshotBuilder.FromRaw(instruments, raw, scoring);
            }

            Dictionary<long, string> processed = await SnapshotSource.LoadProcessedReportsAsync(conn, tx, token);
            return SnapshotBuilder.FromProcessed(instruments, processed, scoring);
        }, ct);

        string outPath = parsed.Get(OutOption) ?? DefaultOutputPath(label);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (directory is not null)
            _ = Directory.CreateDirectory(directory);

        await using (var writer = new StreamWriter(outPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            snapshot.Write(writer);

        LogSummary(logger, snapshot, stopwatch.Elapsed);
        await context.Output.WriteLineAsync(outPath);
        return ExitCodes.Ok;
    }

    private static void LogSummary(ILogger logger, Snapshot snapshot, TimeSpan elapsed) {
        int ok = snapshot.Rows.Count(r => r[Snapshot.StatusColumn] == SnapshotRowBuilder.StatusOk);
        int[] scores = snapshot.Rows
            .Where(r => r[Snapshot.StatusColumn] == SnapshotRowBuilder.StatusOk)
            .Select(r => int.Parse(r[Snapshot.ScoreColumn], CultureInfo.InvariantCulture))
            .ToArray();

        logger.LogInformation(
            "Wrote {Rows} rows in {Seconds:0.0}s: {Ok} scored, {NotScored} not scored; 14+: {Fourteen}, 15+: {Fifteen}, 16/16: {Sixteen}",
            snapshot.Rows.Count, elapsed.TotalSeconds, ok, snapshot.Rows.Count - ok,
            scores.Count(s => s >= 14), scores.Count(s => s >= 15), scores.Count(s => s >= 16));

        foreach (IGrouping<string, IReadOnlyDictionary<string, string>> group in snapshot.Rows
                     .Where(r => r[Snapshot.StatusColumn] != SnapshotRowBuilder.StatusOk)
                     .GroupBy(r => r[Snapshot.StatusColumn])) {
            logger.LogWarning("Not scored ({Status}): {Symbols}", group.Key, string.Join(", ", group.Select(r => r[Snapshot.SymbolColumn])));
        }
    }

    private static string DefaultOutputPath(string label) {
        string root = RunGit("rev-parse --show-toplevel") ?? Directory.GetCurrentDirectory();
        string sha = RunGit("rev-parse --short HEAD") ?? "nogit";
        string? status = RunGit("status --porcelain --untracked-files=no");
        string dirty = string.IsNullOrEmpty(status) ? string.Empty : "-dirty";
        string time = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(root, "snapshots", $"{time}-{sha}{dirty}-{label}.csv");
    }

    /// <returns>Trimmed stdout, or null if git is unavailable or fails.</returns>
    private static string? RunGit(string arguments) {
        try {
            var startInfo = new ProcessStartInfo("git", arguments) {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using Process? process = Process.Start(startInfo);
            if (process is null)
                return null;
            Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            _ = error.Result;
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        } catch (System.ComponentModel.Win32Exception) {
            return null;
        }
    }

    private static string SanitizeLabel(string label) {
        string cleaned = new(label.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return cleaned.Length == 0 ? "snapshot" : cleaned;
    }
}
