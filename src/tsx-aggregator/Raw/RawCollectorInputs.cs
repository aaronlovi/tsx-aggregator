using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator;

public abstract class RawCollectorInputBase : IDisposable {
    private bool _isDisposed;

    public RawCollectorInputBase(long reqId, CancellationTokenSource? cancellationTokenSource) {
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
    // ~RawCollectorInputBase()
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

public sealed class RawCollectorTimeoutInput : RawCollectorInputBase {
    public RawCollectorTimeoutInput(long reqId, CancellationTokenSource? cts, DateTime curTimeUtc)
        : base(reqId, cts) =>
        CurTimeUtc = curTimeUtc;

    public DateTime CurTimeUtc { get; init; }
}

public sealed class RawCollectorPauseServiceInput : RawCollectorInputBase {
    public RawCollectorPauseServiceInput(long reqId, bool pauseNotResume, CancellationTokenSource? cts)
        : base(reqId, cts)
        => PauseNotResume = pauseNotResume;

    public bool PauseNotResume { get; init; }
}

public sealed class RawCollectorGetInstrumentsWithNoRawReportsInput : RawCollectorInputBase {
    public RawCollectorGetInstrumentsWithNoRawReportsInput(
        long reqId, string exchange, int pageNumber, int pageSize, CancellationTokenSource? cts)
        : base(reqId, cts) {
        Exchange = exchange;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public string Exchange { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
}
