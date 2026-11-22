using System;
using System.Collections.Generic;
using System.Threading;

namespace tsx_aggregator.Services;

public abstract class QuoteServiceInputBase : ServiceInputBase {
    protected QuoteServiceInputBase(long reqId, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) { }
}

public sealed class QuoteServiceTimeoutInput : QuoteServiceInputBase {
    public QuoteServiceTimeoutInput(long reqId, CancellationTokenSource? cancellationTokenSource, DateTime curTimeUtc)
        : base(reqId, cancellationTokenSource) => 
        CurTimeUtc = curTimeUtc;

    public DateTime CurTimeUtc { get; init; }
}

public sealed class QuoteServiceFillPricesForSymbolsInput : QuoteServiceInputBase
{
    public QuoteServiceFillPricesForSymbolsInput(long reqId, CancellationTokenSource? cancellationTokenSource, IReadOnlyList<string> symbols)
        : base(reqId, cancellationTokenSource) =>
        Symbols = symbols;

    public IReadOnlyList<string> Symbols { get; init; }
}
