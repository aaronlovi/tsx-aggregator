using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>
/// Operations-support gRPC endpoints (see admin.proto), used by tsx-ops. Pausing a service that runs in this
/// process goes through the service itself, so its in-memory state and the persisted state stay in step.
/// For a service that does not run in this process, the state is saved directly and applies when it next starts.
/// </summary>
internal sealed class AdminSvc : AdminService.AdminServiceBase {
    private readonly IReadOnlyList<IPausableService> _services;
    private readonly HashSet<string> _runningServiceNames;
    private readonly IDbmService _dbm;
    private readonly ILogger<AdminSvc> _logger;

    public AdminSvc(
        IEnumerable<IPausableService> services,
        IOptions<HostedServicesOptions> hostedServices,
        IDbmService dbm,
        ILogger<AdminSvc> logger) {

        _services = services.ToList();
        _dbm = dbm;
        _logger = logger;

        HostedServicesOptions options = hostedServices.Value;
        _runningServiceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IPausableService service in _services) {
            // Matches the hosted-service switches in Program.BuildHost.
            bool running = service.ServiceName switch {
                "Aggregator" => options.RunAggregator ?? false,
                "RawCollector" => options.RunRawCollector ?? false,
                _ => false,
            };
            if (running)
                _ = _runningServiceNames.Add(service.ServiceName);
        }
    }

    public override async Task<GetServiceStatusReply> GetServiceStatus(GetServiceStatusRequest request, ServerCallContext context) {
        var reply = new GetServiceStatusReply { Success = true };
        foreach (IPausableService service in _services.OrderBy(s => s.ServiceName, StringComparer.Ordinal)) {
            (Result res, bool isPaused) = await GetPausedState(service, context.CancellationToken);
            if (!res.Success)
                return new GetServiceStatusReply { Success = false, ErrorMessage = $"{service.ServiceName}: {res.ErrMsg}" };

            reply.Services.Add(new ServiceStatus {
                ServiceName = service.ServiceName,
                IsRunning = IsRunning(service),
                IsPaused = isPaused,
            });
        }
        return reply;
    }

    public override async Task<SetServicePausedReply> SetServicePaused(SetServicePausedRequest request, ServerCallContext context) {
        CancellationToken ct = context.CancellationToken;
        IPausableService? service = _services.FirstOrDefault(s => string.Equals(s.ServiceName, request.ServiceName, StringComparison.OrdinalIgnoreCase));
        if (service is null) {
            string known = string.Join(", ", _services.Select(s => s.ServiceName).Order(StringComparer.Ordinal));
            return new SetServicePausedReply { Success = false, ErrorMessage = $"Unknown service '{request.ServiceName}'. Pausable services: {known}" };
        }

        try {
            (Result res, bool wasPaused) = await GetPausedState(service, ct);
            if (!res.Success)
                return new SetServicePausedReply { Success = false, ErrorMessage = res.ErrMsg };

            bool running = IsRunning(service);
            _logger.LogWarning("AdminSvc - {Action} {ServiceName} (running: {Running}, was paused: {WasPaused})",
                request.Paused ? "pause" : "resume", service.ServiceName, running, wasPaused);

            if (running) {
                await service.SetPausedAsync(request.Paused, ct);
            } else if (wasPaused != request.Paused) {
                res = await _dbm.PersistCommonServiceState(request.Paused, service.ServiceName, ct);
                if (!res.Success)
                    return new SetServicePausedReply { Success = false, ErrorMessage = res.ErrMsg, WasPaused = wasPaused, IsPaused = wasPaused };
            }

            (res, bool isPaused) = await GetPausedState(service, ct);
            return new SetServicePausedReply {
                Success = res.Success,
                ErrorMessage = res.ErrMsg,
                WasPaused = wasPaused,
                IsPaused = isPaused,
                AppliedToRunningService = running,
            };
        } catch (OperationCanceledException) {
            return new SetServicePausedReply { Success = false, ErrorMessage = "Canceled" };
        } catch (Exception ex) {
            _logger.LogError(ex, "AdminSvc - SetServicePaused failed for {ServiceName}", service.ServiceName);
            return new SetServicePausedReply { Success = false, ErrorMessage = ex.Message };
        }
    }

    private bool IsRunning(IPausableService service) => _runningServiceNames.Contains(service.ServiceName);

    /// <summary>The live state for a running service; the saved state otherwise.</summary>
    private async Task<(Result, bool)> GetPausedState(IPausableService service, CancellationToken ct) {
        if (IsRunning(service))
            return (Result.SUCCESS, service.IsPaused);
        return await _dbm.GetCommonServiceState(service.ServiceName, ct);
    }
}
