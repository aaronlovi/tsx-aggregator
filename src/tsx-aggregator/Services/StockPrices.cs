using System.Collections.Generic;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>Putting the quote service's prices into a list of companies.</summary>
internal static class StockPrices {
    public static List<string> InstrumentSymbols(GetStocksDataReply reply) {
        var symbols = new List<string>(reply.StocksData.Count);
        foreach (GetStocksDataReplyItem item in reply.StocksData)
            symbols.Add(item.InstrumentSymbol);
        return symbols;
    }

    /// <summary>Sets each company's price from <paramref name="prices"/> and removes the companies without one.</summary>
    /// <returns>The number of companies removed.</returns>
    public static int FillAndDropUnpriced(GetStocksDataReply reply, IDictionary<string, decimal> prices) {
        int numItemsMissingPrices = 0;
        for (int i = reply.StocksData.Count - 1; i >= 0; i--) {
            GetStocksDataReplyItem item = reply.StocksData[i];
            if (prices.TryGetValue(item.InstrumentSymbol, out decimal price)) {
                item.PerSharePrice = price;
            } else {
                reply.StocksData.RemoveAt(i);
                numItemsMissingPrices++;
            }
        }
        return numItemsMissingPrices;
    }
}
