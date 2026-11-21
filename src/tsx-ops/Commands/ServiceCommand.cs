using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using tsx_aggregator.Services;

namespace tsx_ops.Commands;

/// <summary>
/// Shows and changes the paused state of tsx-aggregator's background services through its AdminService gRPC API.
/// Changes are dry runs unless --execute is given.
/// </summary>
internal sealed class ServiceCommand : ICommand {
    private const string ExecuteFlag = "--execute";
    private const string GrpcOption = "--grpc";

    private readonly Func<Uri, AdminService.AdminServiceClient> _clientFactory;

    public ServiceCommand() : this(CreateClient) { }

    internal ServiceCommand(Func<Uri, AdminService.AdminServiceClient> clientFactory) => _clientFactory = clientFactory;

    public string Name => "service";

    public string Description => "Show, pause or resume tsx-aggregator's background services (changes need --execute)";

    public string Usage => """
        Usage: tsx-ops service status [--grpc <url>]
               tsx-ops service pause <service> [--execute] [--grpc <url>]
               tsx-ops service resume <service> [--execute] [--grpc <url>]

        Talks to tsx-aggregator's AdminService over gRPC, so tsx-aggregator must be running.

          status              List pausable services, whether each runs in that process, and whether it is paused
          pause <service>     Pause a service (e.g. RawCollector, Aggregator)
          resume <service>    Resume a service
          --execute           Apply the change. Without it, pause and resume only report what they would do
          --grpc <url>        tsx-aggregator gRPC address (default: Ops:AggregatorGrpcUrl, http://localhost:7001)

        A running service changes immediately. A service that is not running in that process has its saved state
        changed, which applies when it next starts. Either way the state is saved and survives restarts.
        """;

    public async Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct) {
        ParsedArgs parsed = ParsedArgs.Parse(args, [GrpcOption], [ExecuteFlag]);
        if (parsed.Positionals.Count == 0)
            throw new UsageException("service needs a subcommand: status, pause or resume");

        string subcommand = parsed.Positionals[0];
        Uri address = context.GetAggregatorGrpcUrl(parsed.Get(GrpcOption));
        AdminService.AdminServiceClient client = _clientFactory(address);
        ILogger logger = context.CreateLogger<ServiceCommand>();

        try {
            return subcommand switch {
                "status" when parsed.Positionals.Count == 1 => await StatusAsync(client, context, ct),
                "pause" or "resume" when parsed.Positionals.Count == 2 =>
                    await SetPausedAsync(client, context, logger, parsed.Positionals[1], subcommand == "pause", parsed.Has(ExecuteFlag), ct),
                "status" => throw new UsageException("status takes no arguments"),
                "pause" or "resume" => throw new UsageException($"{subcommand} needs exactly one service name"),
                _ => throw new UsageException($"Unknown subcommand '{subcommand}'"),
            };
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded) {
            logger.LogError("Cannot reach tsx-aggregator at {Address}: {Detail}. Is it running?", address, ex.Status.Detail);
            return ExitCodes.Error;
        }
    }

    private static async Task<int> StatusAsync(AdminService.AdminServiceClient client, OpsContext context, CancellationToken ct) {
        GetServiceStatusReply reply = await client.GetServiceStatusAsync(new GetServiceStatusRequest(), cancellationToken: ct);
        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return ExitCodes.Error;
        }

        int width = Math.Max("Service".Length, reply.Services.Select(s => s.ServiceName.Length).DefaultIfEmpty(0).Max());
        await context.Output.WriteLineAsync($"{"Service".PadRight(width)}  Running  Paused");
        foreach (ServiceStatus s in reply.Services)
            await context.Output.WriteLineAsync($"{s.ServiceName.PadRight(width)}  {YesNo(s.IsRunning),-7}  {YesNo(s.IsPaused)}");
        return ExitCodes.Ok;
    }

    private static async Task<int> SetPausedAsync(
        AdminService.AdminServiceClient client, OpsContext context, ILogger logger,
        string serviceName, bool pause, bool execute, CancellationToken ct) {

        string verb = pause ? "pause" : "resume";
        GetServiceStatusReply status = await client.GetServiceStatusAsync(new GetServiceStatusRequest(), cancellationToken: ct);
        if (!status.Success) {
            await context.Output.WriteLineAsync($"Error: {status.ErrorMessage}");
            return ExitCodes.Error;
        }

        ServiceStatus? current = status.Services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        if (current is null) {
            string known = string.Join(", ", status.Services.Select(s => s.ServiceName));
            throw new UsageException($"Unknown service '{serviceName}'. Pausable services: {known}");
        }

        string where = current.IsRunning ? "running now, takes effect immediately" : "not running, applies when it next starts";
        if (current.IsPaused == pause) {
            await context.Output.WriteLineAsync($"{current.ServiceName} is already {(pause ? "paused" : "not paused")}; nothing to do.");
            return ExitCodes.Ok;
        }

        if (!execute) {
            await context.Output.WriteLineAsync($"Dry run: would {verb} {current.ServiceName} ({where}). Re-run with --execute to apply.");
            return ExitCodes.Ok;
        }

        logger.LogWarning("Requesting {Verb} of {ServiceName} ({Where})", verb, current.ServiceName, where);
        SetServicePausedReply reply = await client.SetServicePausedAsync(
            new SetServicePausedRequest { ServiceName = current.ServiceName, Paused = pause }, cancellationToken: ct);

        if (!reply.Success) {
            await context.Output.WriteLineAsync($"Error: {reply.ErrorMessage}");
            return ExitCodes.Error;
        }

        string applied = reply.AppliedToRunningService ? "applied to the running service" : "saved; applies when it next starts";
        await context.Output.WriteLineAsync(
            $"{current.ServiceName}: paused {YesNo(reply.WasPaused).ToLowerInvariant()} -> {YesNo(reply.IsPaused).ToLowerInvariant()} ({applied}).");
        return reply.IsPaused == pause ? ExitCodes.Ok : ExitCodes.Error;
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static AdminService.AdminServiceClient CreateClient(Uri address) =>
        new(GrpcChannel.ForAddress(address));
}
