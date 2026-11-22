using System;
using System.Threading;
using tsx_aggregator.Services;

namespace tsx_aggregator;

public abstract class AggregatorInputBase : ServiceInputBase {
    protected AggregatorInputBase(long reqId, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) { }
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
