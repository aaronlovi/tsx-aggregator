using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

/// <summary>
/// A background service that can be paused and resumed at runtime. Its paused state is persisted in
/// service_state (keyed by <see cref="ServiceName"/>) and restored when it starts.
/// </summary>
internal interface IPausableService {
    string ServiceName { get; }

    /// <summary>The live paused state. Only meaningful while the service is running.</summary>
    bool IsPaused { get; }

    /// <summary>Pauses or resumes the running service and persists the new state. Completes when persisted.</summary>
    Task SetPausedAsync(bool paused, CancellationToken ct);
}
