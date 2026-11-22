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
/// Shows and replaces the RawCollector's priority queue (companies to scrape next) through tsx-aggregator's gRPC API.
/// The site's System Controls page cannot do this in production, where the web API is read-only. Changes are dry runs
/// unless --execute is given.
/// </summary>
internal sealed class PriorityCommand : ICommand {
    private const string ExecuteFlag = "--execute";
    private const string GrpcOption = "--grpc";

    private readonly Func<Uri, StockDataService.StockDataServiceClient> _clientFactory;

    public PriorityCommand() : this(CreateClient) { }

    internal PriorityCommand(Func<Uri, StockDataService.StockDataServiceClient> clientFactory) => _clientFactory = clientFactory;

    public string Name => "priority";

    public string Description => "Show, set or clear the companies to scrape next (changes need --execute)";

    public string Usage => """
        Usage: tsx-ops priority show [--grpc <url>]
               tsx-ops priority set <symbol>[,<symbol>...] [<symbol> ...] [--execute] [--grpc <url>]
               tsx-ops priority clear [--execute] [--grpc <url>]

        Talks to tsx-aggregator over gRPC, so tsx-aggregator must be running.

          show                List the queue, in the order they will be scraped
          set <symbols>       Replace the queue with these company symbols (e.g. RY,TD or RY TD); upper-cased,
                              duplicates dropped. The scraper starts on them right away
          clear               Empty the queue; scraping goes back to its normal round
          --execute           Apply the change. Without it, set and clear only report what they would do
          --grpc <url>        tsx-aggregator gRPC address (default: Ops:AggregatorGrpcUrl, http://localhost:7001)

        The queue lives in tsx-aggregator's memory: a restart empties it.
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [GrpcOption], [ExecuteFlag]);
        if (parsed.Positionals.Count == 0)
            throw new UsageException("priority needs a subcommand: show, set or clear");

        string subcommand = parsed.Positionals[0];
        Uri address = context.GetAggregatorGrpcUrl(parsed.Get(GrpcOption));
        StockDataService.StockDataServiceClient client = _clientFactory(address);
        ILogger logger = context.CreateLogger<PriorityCommand>();

        try {
            return subcommand switch {
                "show" when parsed.Positionals.Count == 1 => await ShowAsync(client, context, ct),
                "set" when parsed.Positionals.Count > 1 =>
                    await ReplaceAsync(client, context, logger, ParseSymbols(parsed.Positionals.Skip(1)), parsed.Has(ExecuteFlag), ct),
                "clear" when parsed.Positionals.Count == 1 => await ReplaceAsync(client, context, logger, [], parsed.Has(ExecuteFlag), ct),
                "show" or "clear" => throw new UsageException($"{subcommand} takes no arguments"),
                "set" => throw new UsageException("set needs at least one company symbol"),
                _ => throw new UsageException($"Unknown subcommand '{subcommand}'"),
            };
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
            logger.LogError("Cannot reach tsx-aggregator at {Address}: {Detail}. Is it running?", address, ex.Status.Detail);
            return ExitCodes.Error;
        }
    }

    /// <summary>Symbols from "RY,TD" or "RY TD" (or both), upper-cased, in order, without blanks or duplicates.</summary>
    internal static IReadOnlyList<string> ParseSymbols(IEnumerable<string> args) =>
        args.SelectMany(a => a.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static async Task<int> ShowAsync(StockDataService.StockDataServiceClient client, OpsContext context, CancellationToken ct) {
        (bool ok, IReadOnlyList<string> queue) = await GetQueueAsync(client, context, ct);
        if (!ok)
            return ExitCodes.Error;

        await context.Output.WriteLineAsync(queue.Count == 0
            ? "The priority queue is empty."
            : $"{queue.Count} in the priority queue: {string.Join(", ", queue)}");
        return ExitCodes.Ok;
    }

    private static async Task<int> ReplaceAsync(
        StockDataService.StockDataServiceClient client, OpsContext context, ILogger logger,
        IReadOnlyList<string> symbols, bool execute, CancellationToken ct) {

        (bool ok, IReadOnlyList<string> current) = await GetQueueAsync(client, context, ct);
        if (!ok)
            return ExitCodes.Error;

        string from = current.Count == 0 ? "(empty)" : string.Join(", ", current);
        string to = symbols.Count == 0 ? "(empty)" : string.Join(", ", symbols);
        if (!execute) {
            await context.Output.WriteLineAsync($"Dry run: would replace the priority queue {from} with {to}. Re-run with --execute to apply.");
            return ExitCodes.Ok;
        }

        logger.LogWarning("Replacing the priority queue {From} with {To}", from, to);
        var request = new SetPriorityCompaniesRequest();
        request.CompanySymbols.Add(symbols);
        StockDataServiceReply reply = await client.SetPriorityCompaniesAsync(request, cancellationToken: ct);
        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return ExitCodes.Error;
        }

        // Read it back: the reply does not say which symbols the registry recognised.
        (ok, IReadOnlyList<string> now) = await GetQueueAsync(client, context, ct);
        if (!ok)
            return ExitCodes.Error;
        await context.Output.WriteLineAsync(now.Count == 0
            ? "The priority queue is now empty."
            : $"The priority queue is now: {string.Join(", ", now)}");
        return ExitCodes.Ok;
    }

    private static async Task<(bool Ok, IReadOnlyList<string> Queue)> GetQueueAsync(
        StockDataService.StockDataServiceClient client, OpsContext context, CancellationToken ct) {
        GetPriorityCompaniesReply reply = await client.GetPriorityCompaniesAsync(new GetPriorityCompaniesRequest(), cancellationToken: ct);
        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return (false, []);
        }
        return (true, reply.CompanySymbols.ToList());
    }

    private static StockDataService.StockDataServiceClient CreateClient(Uri address) =>
        new(GrpcChannel.ForAddress(address));
}
