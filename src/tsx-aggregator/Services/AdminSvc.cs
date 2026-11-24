using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly bool _aggregatorRuns;
    private readonly IDbmService _dbm;
    private readonly ILogger<AdminSvc> _logger;
    private readonly IEventQueueWaker? _eventQueueWaker;

    public AdminSvc(
        IEnumerable<IPausableService> services,
        IOptions<HostedServicesOptions> hostedServices,
        IDbmService dbm,
        ILogger<AdminSvc> logger,
        IEventQueueWaker? eventQueueWaker = null) {

        _services = services.ToList();
        _dbm = dbm;
        _logger = logger;
        _eventQueueWaker = eventQueueWaker;
        _aggregatorRuns = hostedServices.Value.RunAggregator ?? false;

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

    public override async Task<RequeueInstrumentsReply> RequeueInstruments(RequeueInstrumentsRequest request, ServerCallContext context) {
        CancellationToken ct = context.CancellationToken;
        if (!request.All && request.InstrumentSymbols.Count == 0)
            return new RequeueInstrumentsReply { Success = false, ErrorMessage = "Give instrument symbols, or all" };

        try {
            (Result res, IReadOnlyList<RequeueCandidateDto> candidates) = await _dbm.GetRequeueCandidates(ct);
            if (!res.Success)
                return new RequeueInstrumentsReply { Success = false, ErrorMessage = res.ErrMsg };

            RequeuePlan plan = RequeuePlanner.Plan(candidates, request.All ? null : request.InstrumentSymbols);
            var reply = new RequeueInstrumentsReply { Success = true };
            reply.QueuedSymbols.AddRange(plan.ToQueue.Select(c => c.InstrumentSymbol));
            reply.AlreadyPendingSymbols.AddRange(plan.AlreadyPending);
            reply.NoRawDataSymbols.AddRange(plan.NoRawData);
            reply.UnknownSymbols.AddRange(plan.Unknown);

            // A typo shouldn't half-run a requeue: unknown symbols queue nothing.
            if (plan.Unknown.Count > 0) {
                reply.Success = false;
                reply.ErrorMessage = $"Not listed instruments: {string.Join(", ", plan.Unknown)}. Nothing queued.";
                return reply;
            }

            if (!request.Execute || plan.ToQueue.Count == 0)
                return reply;

            _logger.LogWarning("AdminSvc - requeue: queueing RawDataChanged for {Count} instruments", plan.ToQueue.Count);
            res = await _dbm.InsertRawDataChangedEvents([.. plan.ToQueue.Select(c => c.InstrumentId)], ct);
            if (!res.Success)
                return new RequeueInstrumentsReply { Success = false, ErrorMessage = res.ErrMsg };

            reply.Executed = true;
            if (_aggregatorRuns && _eventQueueWaker is not null) {
                _eventQueueWaker.CheckForEventsNow();
                reply.AggregatorWoken = true;
            }
            return reply;
        } catch (OperationCanceledException) {
            return new RequeueInstrumentsReply { Success = false, ErrorMessage = "Canceled" };
        } catch (Exception ex) {
            _logger.LogError(ex, "AdminSvc - RequeueInstruments failed");
            return new RequeueInstrumentsReply { Success = false, ErrorMessage = ex.Message };
        }
    }

    public override async Task<RepairMergedReportsReply> RepairMergedReports(RepairMergedReportsRequest request, ServerCallContext context) {
        CancellationToken ct = context.CancellationToken;
        try {
            (Result res, IReadOnlyList<MergedReportCandidateDto> candidates) = await _dbm.GetMergedReportCandidates(ct);
            if (!res.Success)
                return new RepairMergedReportsReply { Success = false, ErrorMessage = res.ErrMsg };

            MergedReportPlan plan = MergedReportPlanner.Plan(candidates, request.InstrumentSymbols);
            var reply = new RepairMergedReportsReply { Success = true };
            reply.Reports.AddRange(plan.Items.Select(ToMergedReport));
            reply.NotFoundSymbols.AddRange(plan.NotFoundSymbols);

            // A typo shouldn't half-run a repair: a symbol with nothing to show resets nothing.
            if (plan.NotFoundSymbols.Count > 0) {
                reply.Success = false;
                reply.ErrorMessage = $"No merged reports found for: {string.Join(", ", plan.NotFoundSymbols)}. Nothing reset.";
                return reply;
            }

            long[] toReset = [.. plan.Items.Where(i => i.Reset).Select(i => i.Report.InstrumentReportId)];
            if (!request.Execute || toReset.Length == 0)
                return reply;

            _logger.LogWarning("AdminSvc - merged reports: resetting {Count} raw reports: {InstrumentReportIds}", toReset.Length, toReset);
            res = await _dbm.ResetRawReports(toReset, ct);
            if (!res.Success)
                return new RepairMergedReportsReply { Success = false, ErrorMessage = res.ErrMsg };

            reply.Executed = true;
            if (_aggregatorRuns && _eventQueueWaker is not null) {
                _eventQueueWaker.CheckForEventsNow();
                reply.AggregatorWoken = true;
            }
            return reply;
        } catch (OperationCanceledException) {
            return new RepairMergedReportsReply { Success = false, ErrorMessage = "Canceled" };
        } catch (Exception ex) {
            _logger.LogError(ex, "AdminSvc - RepairMergedReports failed");
            return new RepairMergedReportsReply { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static MergedReport ToMergedReport(MergedReportPlanItem item) => new() {
        CompanySymbol = item.Report.CompanySymbol,
        InstrumentSymbol = item.Report.InstrumentSymbol,
        InstrumentReportId = item.Report.InstrumentReportId,
        ReportType = item.Report.ReportType,
        ReportPeriodType = item.Report.ReportPeriodType,
        ReportDate = item.Report.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        OtherReportDate = item.OtherReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Finding = item.Finding,
        Reset = item.Reset,
    };

    private bool IsRunning(IPausableService service) => _runningServiceNames.Contains(service.ServiceName);

    /// <summary>The live state for a running service; the saved state otherwise.</summary>
    private async Task<(Result, bool)> GetPausedState(IPausableService service, CancellationToken ct) {
        if (IsRunning(service))
            return (Result.SUCCESS, service.IsPaused);
        return await _dbm.GetCommonServiceState(service.ServiceName, ct);
    }
}
