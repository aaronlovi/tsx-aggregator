using System;
using System.Threading;

namespace tsx_aggregator.Services;

public abstract class StocksDataRequestsInputBase : ServiceInputBase {
    protected StocksDataRequestsInputBase(long reqId, CancellationTokenSource? cancellationTokenSource)
        : base(reqId, cancellationTokenSource) { }
}

public sealed class GetStocksForExchangeRequest : StocksDataRequestsInputBase {
    public GetStocksForExchangeRequest(long reqId, string exchange, CancellationTokenSource? cts) : base(reqId, cts) {
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentNullException(nameof(exchange));
        Exchange = exchange;
    }

    public string Exchange { get; init; }
}

public sealed class GetDashboardStatsRequestInput : StocksDataRequestsInputBase {
    public GetDashboardStatsRequestInput(long reqId, CancellationTokenSource? cts) : base(reqId, cts) { }
}

public sealed class GetStockDetailsForExchangeRequest : StocksDataRequestsInputBase {
    public GetStockDetailsForExchangeRequest(long reqId, string exchange, string instrumentSymbol, CancellationTokenSource? cts) : base(reqId, cts) {
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentNullException(nameof(exchange));
        if (string.IsNullOrWhiteSpace(instrumentSymbol))
            throw new ArgumentNullException(nameof(instrumentSymbol));
        Exchange = exchange;
        InstrumentSymbol = instrumentSymbol;
    }

    public string Exchange { get; init; }
    public string InstrumentSymbol { get; init; }
}
