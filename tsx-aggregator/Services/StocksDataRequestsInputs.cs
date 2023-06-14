using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

public abstract class StocksDataRequestsInputBase : IDisposable {
    private bool _isDisposed;

    public StocksDataRequestsInputBase(long reqId, CancellationTokenSource? cancellationTokenSource) {
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
    // ~StocksDataRequestsInputBase()
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

public sealed class GetStocksForExchangeRequest : StocksDataRequestsInputBase {
    public GetStocksForExchangeRequest(long reqId, string exchange, CancellationTokenSource? cts) : base(reqId, cts) {
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentNullException(nameof(exchange));
        Exchange = exchange;
    }

    public string Exchange { get; init; }
}
