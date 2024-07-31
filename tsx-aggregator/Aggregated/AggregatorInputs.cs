using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator;

public abstract class AggregatorInputBase : IDisposable {
    private bool _isDisposed;

    public AggregatorInputBase(long reqId, CancellationTokenSource? cancellationTokenSource) {
        ReqId = reqId;
        Completed = new();
        CancellationTokenSource = cancellationTokenSource;
    }

    public long ReqId { get; init; }

    [JsonIgnore]
    public TaskCompletionSource<object?> Completed { get; init; }

    [JsonIgnore]
    public CancellationTokenSource? CancellationTokenSource { get; init; }

    protected virtual void Dispose(bool disposing) {
        if (_isDisposed) {
            return;
        }

        if (disposing) {
            // Dispose managed state (managed objects)
            CancellationTokenSource?.Dispose();
        }

        // Free unmanaged resources (unmanaged objects) and override finalizer

        // Set large fields to null

        _isDisposed = true;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~AggregatorInputBase()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public sealed class AggregatorTimeoutInput : AggregatorInputBase {
    public AggregatorTimeoutInput(long reqId, CancellationTokenSource? cts, DateTime curTimeUtc)
        : base(reqId, cts) =>
        CurTimeUtc = curTimeUtc;

    public DateTime CurTimeUtc { get; init; }
}

public sealed class AggregatorPauseServiceInput : AggregatorInputBase {
    public AggregatorPauseServiceInput(long reqId, bool pauseNotResume, CancellationTokenSource? cts)
        : base(reqId, cts)
        => PauseNotResume = pauseNotResume;

    public bool PauseNotResume { get; init; }
}
