using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

/// <summary>
/// A request posted to a background service's input channel. The poster awaits <see cref="Completed"/>, and the
/// service sets it when the request is done, has failed, or was cancelled.
/// </summary>
/// <remarks>
/// Each service has its own abstract subclass (AggregatorInputBase, RawCollectorInputBase, and so on), so its
/// channel only accepts that service's requests.
/// </remarks>
public abstract class ServiceInputBase : IDisposable {
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private bool _isDisposed;

    protected ServiceInputBase(long reqId, CancellationTokenSource? cancellationTokenSource) {
        ReqId = reqId;
        _cancellationTokenSource = cancellationTokenSource;

        // Captured now: a poster that gives up disposes this input, and the token source with it, while the input may
        // still be queued. Reading Token from a disposed source throws; a token captured earlier keeps working.
        CancellationToken = cancellationTokenSource?.Token ?? CancellationToken.None;
    }

    public long ReqId { get; init; }

    /// <summary>
    /// Set by the service. Continuations run asynchronously, so the poster's code never runs on the service's loop.
    /// </summary>
    [JsonIgnore]
    public TaskCompletionSource<object?> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Cancelled when the poster gives up on this request.</summary>
    [JsonIgnore]
    public CancellationToken CancellationToken { get; }

    public void Dispose() {
        if (_isDisposed)
            return;

        _cancellationTokenSource?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
