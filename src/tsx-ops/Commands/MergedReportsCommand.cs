using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using tsx_aggregator.Services;
using static tsx_aggregator.shared.Constants;

namespace tsx_ops.Commands;

/// <summary>
/// Finds and resets raw reports that were merged with another report before review R31, through tsx-aggregator's
/// AdminService. A dry run unless --execute is given.
/// </summary>
internal sealed class MergedReportsCommand : ICommand {
    private const string ExecuteFlag = "--execute";
    private const string GrpcOption = "--grpc";

    private readonly Func<Uri, AdminService.AdminServiceClient> _clientFactory;

    public MergedReportsCommand() : this(address => new AdminService.AdminServiceClient(GrpcChannel.ForAddress(address))) { }

    internal MergedReportsCommand(Func<Uri, AdminService.AdminServiceClient> clientFactory) => _clientFactory = clientFactory;

    public string Name => "merged-reports";

    public string Description => "Find raw reports merged with another one before R31, and reset them (needs --execute)";

    public string Usage => """
        Usage: tsx-ops merged-reports [<symbol>...] [--execute] [--grpc <url>]

        Before review R31, two reports of one calendar year (annual) or quarter were merged into one row. This lists
        the rows of such pairs and, with --execute, marks the repairable ones as not current and queues a rebuild of
        their companies. The next scrape inserts a clean copy of each one TMX still sends (the same one if it was not
        merged). Talks to tsx-aggregator's AdminService over gRPC, so tsx-aggregator must be running.

          <symbol>...         Only these instrument symbols (case-insensitive); default: all. One without a merged report
                              resets nothing
          --execute           Reset the rows. Without it, only list them
          --grpc <url>        tsx-aggregator gRPC address (default: Ops:AggregatorGrpcUrl, http://localhost:7001)

        Run it after a full scrape pass on the fixed code: a row TMX still sends has a currency by then, and one it no
        longer sends is left alone. Back up the database first. To undo, for the ids it prints:
          UPDATE instrument_reports SET is_current = TRUE, obsoleted_date = NULL WHERE instrument_report_id IN (...)
        for rows no scrape has replaced yet, and requeue the companies.
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [GrpcOption], [ExecuteFlag]);
        bool execute = parsed.Has(ExecuteFlag);
        Uri address = context.GetAggregatorGrpcUrl(parsed.Get(GrpcOption));
        AdminService.AdminServiceClient client = _clientFactory(address);
        ILogger logger = context.CreateLogger<MergedReportsCommand>();

        var request = new RepairMergedReportsRequest { Execute = execute };
        request.InstrumentSymbols.AddRange(parsed.Positionals);

        RepairMergedReportsReply reply;
        try {
            if (execute)
                logger.LogWarning("Requesting reset of merged reports of {Target}",
                    parsed.Positionals.Count == 0 ? "all instruments" : string.Join(", ", parsed.Positionals));
            reply = await client.RepairMergedReportsAsync(request, cancellationToken: ct);
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
            logger.LogError("Cannot reach tsx-aggregator at {Address}: {Detail}. Is it running?", address, ex.Status.Detail);
            return ExitCodes.Error;
        }

        foreach (MergedReport report in reply.Reports)
            await context.Output.WriteLineAsync(Describe(report));

        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return ExitCodes.Error;
        }
        if (reply.Reports.Count == 0) {
            await context.Output.WriteLineAsync("No merged reports found.");
            return ExitCodes.Ok;
        }

        List<MergedReport> reset = [.. reply.Reports.Where(r => r.Reset)];
        await context.Output.WriteLineAsync($"{(reply.Executed ? "Reset" : "Would reset")} {reset.Count} of {reply.Reports.Count} rows.");
        if (reply.Executed)
            await DescribeNextSteps(context, reset, reply.AggregatorWoken);
        else if (reset.Count > 0)
            await context.Output.WriteLineAsync("Dry run. Re-run with --execute to reset them.");
        return ExitCodes.Ok;
    }

    private static async Task DescribeNextSteps(OpsContext context, List<MergedReport> reset, bool aggregatorWoken) {
        await context.Output.WriteLineAsync($"Row ids, to undo: {string.Join(", ", reset.Select(r => r.InstrumentReportId))}");
        await context.Output.WriteLineAsync(aggregatorWoken
            ? "The Aggregator was told to start now. Watch progress with 'tsx-ops events'."
            : "The Aggregator isn't running in that process; the rebuilds wait until it runs.");

        List<string> companies = [.. reset.Select(r => r.CompanySymbol).Distinct().Order(StringComparer.Ordinal)];
        await context.Output.WriteLineAsync("Get the clean copies now (replaces the priority queue): "
            + $"tsx-ops priority set {string.Join(",", companies)} --execute");

        // The priority queue scrapes one instrument of a company; the others wait for their turn in the normal round.
        List<string> several = [.. reset.GroupBy(r => r.CompanySymbol)
            .Where(g => g.Select(r => r.InstrumentSymbol).Distinct().Count() > 1)
            .Select(g => g.Key).Order(StringComparer.Ordinal)];
        if (several.Count > 0)
            await context.Output.WriteLineAsync("The priority queue scrapes one instrument per company; these have more than one listed "
                + $"instrument here: {string.Join(", ", several)}. The others come back in the normal round.");
    }

    private static string Describe(MergedReport report) =>
        $"{(report.Reset ? "reset" : "leave"),-6}{report.InstrumentSymbol,-8}{Period(report.ReportPeriodType),-11}{Type(report.ReportType),-17}"
        + $"{report.ReportDate}  {Reason(report)}  (row {report.InstrumentReportId})";

    private static string Reason(MergedReport report) => report.Finding switch {
        MergedReportFinding.WrongDate => $"holds the {report.OtherReportDate} report, which is stored under its own date too",
        MergedReportFinding.LeftoverFields => $"may hold fields of the {report.OtherReportDate} report; TMX still sends this one",
        MergedReportFinding.OnlyCopy => $"holds the {report.OtherReportDate} report, which is stored nowhere else",
        MergedReportFinding.NotSent => $"may hold fields of the {report.OtherReportDate} report; TMX has not sent this one since the fix",
        _ => $"merged with the {report.OtherReportDate} report",
    };

    private static string Period(int period) => (ReportPeriodTypes)period switch {
        ReportPeriodTypes.Annual => "annual",
        ReportPeriodTypes.Quarterly => "quarterly",
        _ => $"period {period}",
    };

    private static string Type(int type) => (ReportTypes)type switch {
        ReportTypes.CashFlow => "cash flow",
        ReportTypes.IncomeStatement => "income",
        ReportTypes.BalanceSheet => "balance sheet",
        _ => $"type {type}",
    };
}
