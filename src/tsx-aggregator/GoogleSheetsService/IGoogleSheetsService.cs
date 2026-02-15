using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

internal interface IGoogleSheetsService : IDisposable {
    void ClearColumns();

    Task<Dictionary<string, decimal>> ReadQuotes(string instrumentSymbolColumn, string quoteColumn, string[] fallbackColumns, CancellationToken ct);

    void WriteRawValuesToRange(string column, List<IList<object>> values);

    void WriteUserEnteredValuesToRange(string column, List<IList<object>> values);

    /// <summary>
    /// Fetches any quote overrides into the Google worksheet
    /// </summary>
    /// <remarks>May throw</remarks>
    Task FetchQuoteOverrides(CancellationToken ct);
}
