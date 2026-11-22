using System;
using System.Collections.Generic;
using System.Threading;
using tsx_aggregator.Services;

namespace tsx_aggregator;

public abstract class RawCollectorInputBase : ServiceInputBase {
    protected RawCollectorInputBase(long reqId, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) { }
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
