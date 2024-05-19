using System;
using System.Collections.Generic;
using Google.Apis.Sheets.v4;

namespace tsx_aggregator.Services;

internal interface IGoogleSheetsService : IDisposable {
    void ClearColumns();

    Dictionary<string, decimal> ReadQuotes(string instrumentSymbolColumn, string quoteColumn);

    void WriteRawValuesToRange(string column, List<IList<object>> values);

    void WriteUserEnteredValuesToRange(string column, List<IList<object>> values);
}
