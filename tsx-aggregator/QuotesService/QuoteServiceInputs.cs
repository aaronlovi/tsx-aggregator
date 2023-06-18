using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

public abstract class QuoteServiceInputBase : IDisposable {
    private bool _isDisposed;

    public QuoteServiceInputBase(long reqId, CancellationTokenSource? cancellationTokenSource) {
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
    // ~QuoteServiceInputBase()
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

public sealed class QuoteServiceTimeoutInput : QuoteServiceInputBase {
    public QuoteServiceTimeoutInput(long reqId, CancellationTokenSource? cancellationTokenSource, DateTime curTimeUtc)
        : base(reqId, cancellationTokenSource) {
        CurTimeUtc = curTimeUtc;
    }

    public DateTime CurTimeUtc { get; init; }
}
