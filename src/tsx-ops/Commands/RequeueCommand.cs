using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using tsx_aggregator.Services;

namespace tsx_ops.Commands;

/// <summary>
/// Queues RawDataChanged events so the Aggregator rebuilds processed reports, through tsx-aggregator's AdminService
/// (O1, decision 26). A dry run unless --execute is given.
/// </summary>
internal sealed class RequeueCommand : ICommand {
    private const string ExecuteFlag = "--execute";
    private const string AllFlag = "--all";
    private const string GrpcOption = "--grpc";
    private const int MaxSymbolsShown = 20;

    private readonly Func<Uri, AdminService.AdminServiceClient> _clientFactory;

    public RequeueCommand() : this(address => new AdminService.AdminServiceClient(GrpcChannel.ForAddress(address))) { }

    internal RequeueCommand(Func<Uri, AdminService.AdminServiceClient> clientFactory) => _clientFactory = clientFactory;

    public string Name => "requeue";

    public string Description => "Queue processed-report rebuilds for all or some instruments (needs --execute)";

    public string Usage => """
        Usage: tsx-ops requeue --all [--execute] [--grpc <url>]
               tsx-ops requeue <symbol>... [--execute] [--grpc <url>]

        Queues a RawDataChanged event per listed instrument with raw data, so the Aggregator rebuilds its processed
        report with the current code (e.g. after a scoring change). Talks to tsx-aggregator's AdminService over
        gRPC, so tsx-aggregator must be running; if its Aggregator runs, it starts at once.

          --all               Every listed instrument with raw data
          <symbol>...         These instrument symbols (case-insensitive). Any unknown symbol queues nothing
          --execute           Queue the events. Without it, only report what would be queued
          --grpc <url>        tsx-aggregator gRPC address (default: Ops:AggregatorGrpcUrl, http://localhost:7001)

        Instruments that already have a RawDataChanged event waiting are not queued again. Back up the database
        before a full rebuild (TSX Aggregator Runbook, section 5).
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [GrpcOption], [ExecuteFlag, AllFlag]);
        bool all = parsed.Has(AllFlag);
        if (all && parsed.Positionals.Count > 0)
            throw new UsageException("Give either --all or instrument symbols, not both");
        if (!all && parsed.Positionals.Count == 0)
            throw new UsageException("requeue needs --all or one or more instrument symbols");

        bool execute = parsed.Has(ExecuteFlag);
        Uri address = context.GetAggregatorGrpcUrl(parsed.Get(GrpcOption));
        AdminService.AdminServiceClient client = _clientFactory(address);
        ILogger logger = context.CreateLogger<RequeueCommand>();

        var request = new RequeueInstrumentsRequest { All = all, Execute = execute };
        request.InstrumentSymbols.AddRange(parsed.Positionals);

        RequeueInstrumentsReply reply;
        try {
            if (execute)
                logger.LogWarning("Requesting requeue of {Target}", all ? "all instruments" : string.Join(", ", parsed.Positionals));
            reply = await client.RequeueInstrumentsAsync(request, cancellationToken: ct);
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
            logger.LogError("Cannot reach tsx-aggregator at {Address}: {Detail}. Is it running?", address, ex.Status.Detail);
            return ExitCodes.Error;
        }

        string verb = reply.Executed ? "Queued" : "Would queue";
        await context.Output.WriteLineAsync($"{verb} {reply.QueuedSymbols.Count}: {Symbols(reply.QueuedSymbols)}");
        if (reply.AlreadyPendingSymbols.Count > 0)
            await context.Output.WriteLineAsync($"Already waiting, not queued again {reply.AlreadyPendingSymbols.Count}: {Symbols(reply.AlreadyPendingSymbols)}");
        if (reply.NoRawDataSymbols.Count > 0)
            await context.Output.WriteLineAsync($"No raw data, nothing to rebuild {reply.NoRawDataSymbols.Count}: {Symbols(reply.NoRawDataSymbols)}");
        if (reply.UnknownSymbols.Count > 0)
            await context.Output.WriteLineAsync($"Not listed {reply.UnknownSymbols.Count}: {Symbols(reply.UnknownSymbols)}");

        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return ExitCodes.Error;
        }

        if (!execute && reply.QueuedSymbols.Count > 0)
            await context.Output.WriteLineAsync("Dry run. Re-run with --execute to queue them.");
        else if (reply.Executed)
            await context.Output.WriteLineAsync(reply.AggregatorWoken
                ? "The Aggregator was told to start now. Watch progress with 'tsx-ops events'."
                : "The Aggregator isn't running in that process; the events wait until it runs.");
        return ExitCodes.Ok;
    }

    private static string Symbols(IReadOnlyCollection<string> symbols) =>
        symbols.Count <= MaxSymbolsShown
            ? string.Join(" ", symbols)
            : string.Join(" ", symbols.Take(MaxSymbolsShown)) + $" ... (+{symbols.Count - MaxSymbolsShown} more)";
}
