using System;
using System.Threading;

namespace tsx_aggregator.Services;

public abstract class SearchServiceInputBase : ServiceInputBase {
    protected SearchServiceInputBase(long reqId, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) { }
}

public sealed class SearchServiceTimeoutInput : SearchServiceInputBase {
    public SearchServiceTimeoutInput(long reqId, CancellationTokenSource? cancellationTokenSource, DateTime curTimeUtc)
        : base(reqId, cancellationTokenSource) {
        CurTimeUtc = curTimeUtc;
    }

    public DateTime CurTimeUtc { get; init; }
}

public sealed class SearchServiceQuickSearchRequestInput : SearchServiceInputBase {
    public SearchServiceQuickSearchRequestInput(long reqId, string searchTerm, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) {
        SearchTerm = searchTerm;
    }

    public string SearchTerm { get; init; }
}
