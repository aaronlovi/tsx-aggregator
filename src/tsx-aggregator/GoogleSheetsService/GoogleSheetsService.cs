using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;

namespace tsx_aggregator.Services;

/// <summary>
/// Provides a service for interacting with Google Sheets, including operations such as reading,
/// writing, and clearing data within a specified spreadsheet. This service encapsulates the
/// complexity of the Google Sheets API, offering a simplified interface for manipulating spreadsheet
/// data in the context of the application.
/// </summary>
/// <remarks>
/// The service requires configuration settings for authentication with Google's API, including
/// the path to the credentials file, the application name, and identifiers for the target spreadsheet.
/// These configurations are read from the application's settings and are validated upon service
/// initialization.
///
/// This service utilizes the Google Sheets API v4 and supports operations like reading quotes from
/// a specified range, writing values to a range either as raw inputs or user-entered values, and
/// clearing columns within the spreadsheet. It is designed to be used as a scoped service within
/// an ASP.NET Core application, ensuring that resources are properly managed and disposed of.
///
/// Usage of this service should be done through its interface, `IGoogleSheetsService`, allowing
/// for easy mocking and testing of components that depend on Google Sheets interactions.
/// 
/// This class does not serialize access to the Google Sheets API, so it is not thread-safe. If
/// multiple threads need to access the service concurrently, then use an appropriate synchronization
/// mechanism to ensure thread safety.
/// </remarks>
/// <example>
/// Example usage for reading quotes from a spreadsheet:
/// <code>
/// var quotes = googleSheetsService.ReadQuotes("A", "B");
/// foreach (var quote in quotes)
/// {
///     Console.WriteLine($"{quote.Key}: {quote.Value}");
/// }
/// </code>
/// </example>
public class GoogleSheetsService : IGoogleSheetsService {
    private readonly GoogleCredentialsOptions _googleCredentials;
    private readonly ILogger _logger;
    private readonly SheetsService _sheetService;
    private readonly HttpClient _httpClient;
    private bool disposedValue;

    private string WebMacroUrl => $"https://script.google.com/macros/s/{_googleCredentials.MacroName}/exec";

    public GoogleSheetsService(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<GoogleSheetsService>>();
        _googleCredentials = svp.GetRequiredService<IOptions<GoogleCredentialsOptions>>().Value;

        IHttpClientFactory httpClientFactory = svp.GetRequiredService<IHttpClientFactory>();
        _httpClient = httpClientFactory.CreateClient();

        // Create the Google credential object
#pragma warning disable CS0618 // GoogleCredential.FromFile is obsolete but replacement API (CredentialFactory) is not yet public
        var credential = GoogleCredential
            .FromFile(_googleCredentials.CredentialFilePath)
            .CreateScoped([SheetsService.Scope.Spreadsheets]);
#pragma warning restore CS0618

        // Initialize the SheetsService instance
        _sheetService = new SheetsService(new BaseClientService.Initializer() {
            HttpClientInitializer = credential,
            ApplicationName = _googleCredentials.GoogleApplicationName
        });

        _logger.LogInformation("GoogleSheetsService - Created");
    }

    public void ClearColumns() {
        var request = new ClearValuesRequest();
        _ = _sheetService.Spreadsheets.Values
            .Clear(request, _googleCredentials.SpreadsheetId, _googleCredentials.SpreadsheetName)
            .Execute();
    }

    public async Task<Dictionary<string, decimal>> ReadQuotes(
        string instrumentSymbolColumn,
        string quoteColumn,
        string[] fallbackColumns,
        CancellationToken ct) {

        var columns = new List<string> { instrumentSymbolColumn, quoteColumn }.Concat(fallbackColumns);
        var maxColumn = GoogleSheetsHelper.FindMaximumColumn(columns);

        var quotes = new Dictionary<string, decimal>();

        // Define a range that will get all rows with data in the spreadsheet
        string range = $"{_googleCredentials.SpreadsheetName}!{instrumentSymbolColumn}1:{maxColumn}";

        var request = _sheetService.Spreadsheets.Values.Get(_googleCredentials.SpreadsheetId, range);
        ValueRange response = await request.ExecuteAsync(ct);

        if (response.Values == null)
            return quotes;

        foreach (IList<object> row in response.Values) {
            if (row.Count < 2)
                continue;
            string instrumentSymbol = row[0]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(instrumentSymbol))
                continue;
            bool found = false;
            decimal perSharePrice = 0;
            for (int i = 1; i < row.Count; i++) {
                if (!decimal.TryParse(row[i].ToString(), out perSharePrice) || perSharePrice <= 0)
                    continue;

                found = true;
                break;
            }
            if (!found)
                continue;
            if (quotes.ContainsKey(instrumentSymbol))
                continue;
            quotes.Add(instrumentSymbol, perSharePrice);
        }

        return quotes;
    }

    public void WriteRawValuesToRange(string column, List<IList<object>> values) =>
        WriteToRange(column, values, SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW);

    public void WriteUserEnteredValuesToRange(string column, List<IList<object>> values) =>
        WriteToRange(column, values, SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED);

    /// <summary>
    /// Fetches a Yahoo finance quote override into column "C" for any instrument
    /// that does not have a valid price
    /// </summary>
    /// <remarks>May throw</remarks>
    public async Task FetchQuoteOverrides(CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(_googleCredentials.MacroName)) {
            _logger.LogWarning("FetchQuoteOverrides - Macro name is not set. Skipping Yahoo finance fetch.");
            return;
        }

        // Define the range to read from, assuming "A" and "B" are in the same sheet as defined in
        // _googleCredentials
        string range = $"{_googleCredentials.SpreadsheetName}!A:B";

        // Prepare the request
        var request = _sheetService.Spreadsheets.Values.Get(_googleCredentials.SpreadsheetId, range);
        ValueRange response = await request.ExecuteAsync(ct);

        // Check if there are values returned
        if (response.Values == null) {
            _logger.LogWarning("FetchQuoteOverrides - No data found.");
            return;
        }

        int rowIndex = 0; // Start from the first row

        // Iterate through each row in the response
        foreach (var row in response.Values) {
            try {
                ++rowIndex;

                // Check if the row is not empty and has at least 2 columns (for "A" and "B")
                if (row.Count < 2
                    || string.IsNullOrWhiteSpace(row[0]?.ToString())
                    || string.IsNullOrWhiteSpace(row[1]?.ToString())) {
                    // If either "A" or "B" is blank, stop processing further
                    break;
                }

                // Extract values from columns "A" and "B"
                string instrumentSymbol = row[0].ToString()!;
                string quote = row[1].ToString()!;

                // If the price is a valid number, then do not fetch any override quotes
                if (double.TryParse(quote, out _))
                    continue;

                _logger.LogInformation("FetchQuoteOverrides - Price is N/A for {InstrumentSymbol}. Fetching from Yahoo finance.",
                    instrumentSymbol);

                // Special instrument symbol processing for Yahoo quotes
                instrumentSymbol = instrumentSymbol.Replace(".", "-");

                string webAppUrl = $"{WebMacroUrl}?ticker={instrumentSymbol}.TO";
                HttpResponseMessage res = await _httpClient.GetAsync(webAppUrl, ct);
                if (!res.IsSuccessStatusCode) {
                    _logger.LogWarning("FetchQuoteOverrides - Failed to fetch instrument price for {InstrumentSymbol} from Yahoo finance.",
                        instrumentSymbol);
                    continue;
                }

                string price = await res.Content.ReadAsStringAsync(ct);
                if (!double.TryParse(price, out _)) {
                    _logger.LogWarning("FetchQuoteOverrides - Failed to parse instrument price for {InstrumentSymbol} from Yahoo finance. Error: {Error}",
                        instrumentSymbol, price);
                    continue;
                }

                // Prepare the ValueRange object with the override price
                var valueRange = new ValueRange {
                    Values = [[price]]
                };

                // Calculate the cell to update in column C
                string updateRange = $"{_googleCredentials.SpreadsheetName}!C{rowIndex}";

                // Execute the update request
                var updateRequest = _sheetService.Spreadsheets.Values.Update(valueRange, _googleCredentials.SpreadsheetId, updateRange);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                _ = await updateRequest.ExecuteAsync(ct);
            } catch (Exception ex) {
                _logger.LogError(ex, "FetchQuoteOverrides - Error processing row {RowIndex}", rowIndex);
            }
        }
    }

    private void WriteToRange(
        string column,
        List<IList<object>> values,
        SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum valueType) {

        // Set the range of cells to update
        string range = $"{_googleCredentials.SpreadsheetName}!{column}1:{column}{values.Count}";

        // Create the ValueRange object
        var valueRange = new ValueRange { Values = values };

        // Execute the update request
        var request = _sheetService.Spreadsheets.Values
            .Update(valueRange, _googleCredentials.SpreadsheetId, range);
        request.ValueInputOption = valueType;
        _ = request.Execute();
    }

    #region IDISPOSABLE

    protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                _sheetService.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null

            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~GoogleSheetsService()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
