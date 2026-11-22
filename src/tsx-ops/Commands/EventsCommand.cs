using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tsx_ops.Db;
using tsx_ops.Events;

namespace tsx_ops.Commands;

/// <summary>Read-only: the Aggregator's queue of unprocessed instrument events, by type.</summary>
internal sealed class EventsCommand : ICommand {
    public string Name => "events";

    public string Description => "Show the Aggregator's unprocessed event queue by type (read-only)";

    public string Usage => """
        Usage: tsx-ops events [--connection <cs>]

        Counts unprocessed instrument events by type: pending (the Aggregator will pick them up) and stuck (it
        won't: RawDataChanged for a delisted instrument or one without a current price row), with the oldest
        pending event. Read-only; safe while tsx-aggregator runs.

          --connection <cs>   Override ConnectionStrings:tsx-scraper
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [ParsedArgs.ConnectionOption], []);
        if (parsed.Positionals.Count > 0)
            throw new UsageException("events takes no arguments");

        string connectionString = context.GetConnectionString(parsed.Get(ParsedArgs.ConnectionOption));
        List<EventQueueRow> rows = await OpsDb.RunReadOnlyAsync(connectionString, context.StatementTimeoutSeconds,
            (conn, tx, token) => EventQueue.LoadAsync(conn, tx, token), ct);
        await context.Output.WriteAsync(EventQueue.Format(rows));
        return ExitCodes.Ok;
    }
}
