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

/// <summary>
/// Posted by the background fetch cycle once it has read the quotes already in the sheet. The loop merges them and
/// marks the service ready.
/// </summary>
internal sealed class QuoteServiceOldQuotesReadInput : QuoteServiceInputBase {
    public QuoteServiceOldQuotesReadInput(IReadOnlyDictionary<string, decimal> prices, DateTime readUtc)
        : base(reqId: 0, cancellationTokenSource: null) {
        Prices = prices;
        ReadUtc = readUtc;
    }

    public IReadOnlyDictionary<string, decimal> Prices { get; }
    public DateTime ReadUtc { get; }
}

/// <summary>
/// Posted by the background fetch cycle when it ends. The loop merges the new quotes, moves the next fetch time or
/// backs off, and sets the next timeout.
/// </summary>
internal sealed class QuoteServiceFetchDoneInput : QuoteServiceInputBase {
    private QuoteServiceFetchDoneInput(IReadOnlyDictionary<string, decimal>? newPrices, DateTime? readUtc,
        DateTime? nextFetchQuotesTimeUtc, Exception? failure, bool cancelled)
        : base(reqId: 0, cancellationTokenSource: null) {
        NewPrices = newPrices;
        ReadUtc = readUtc;
        NextFetchQuotesTimeUtc = nextFetchQuotesTimeUtc;
        Failure = failure;
        Cancelled = cancelled;
    }

    /// <summary>A cycle that found the fetch not due yet: only the old quotes were read.</summary>
    public static QuoteServiceFetchDoneInput NotDue() => new(null, null, null, null, cancelled: false);

    public static QuoteServiceFetchDoneInput Fetched(IReadOnlyDictionary<string, decimal> newPrices, DateTime readUtc,
        DateTime nextFetchQuotesTimeUtc) =>
        new(newPrices, readUtc, nextFetchQuotesTimeUtc, null, cancelled: false);

    public static QuoteServiceFetchDoneInput Failed(Exception failure) => new(null, null, null, failure, cancelled: false);

    public static QuoteServiceFetchDoneInput WasCancelled() => new(null, null, null, null, cancelled: true);

    public IReadOnlyDictionary<string, decimal>? NewPrices { get; }
    public DateTime? ReadUtc { get; }
    public DateTime? NextFetchQuotesTimeUtc { get; }
    public Exception? Failure { get; }
    public bool Cancelled { get; }
}
