﻿using System;
using System.Collections.Generic;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly SheetsService _sheetService;
    private string? _credentialKeyFileName;
    private string? _googleApplicationName;
    private string? _spreadsheetId;
    private string? _spreadsheetName;
    private bool disposedValue;

    private string CredentialKeyFileName {
        get {
            _credentialKeyFileName ??= _configuration["GoogleCredentials:CredentialFilePath"] ?? string.Empty;
            return _credentialKeyFileName;
        }
    }

    private string GoogleApplicationName {
        get {
            _googleApplicationName ??= _configuration["GoogleCredentials:GoogleApplicationName"] ?? string.Empty;
            return _googleApplicationName;
        }
    }

    private string SpreadsheetId {
        get {
            _spreadsheetId ??= _configuration["GoogleCredentials:SpreadsheetId"] ?? string.Empty;
            return _spreadsheetId;
        }
    }

    private string SpreadsheetName {
        get {
            _spreadsheetName ??= _configuration["GoogleCredentials:SpreadsheetName"] ?? string.Empty;
            return _spreadsheetName;
        }
    }

    public GoogleSheetsService(ILogger<GoogleSheetsService> logger, IConfiguration configuration) {
        _logger = logger;
        _configuration = configuration;

        EnsureConfigurationIsValid();

        // Create the Google credential object
        var credential = GoogleCredential
            .FromFile(CredentialKeyFileName)
            .CreateScoped(new[] { SheetsService.Scope.Spreadsheets });

        // Initialize the SheetsService instance
        _sheetService = new SheetsService(new BaseClientService.Initializer() {
            HttpClientInitializer = credential,
            ApplicationName = GoogleApplicationName
        });

        _logger.LogInformation("GoogleSheetsService - Created");
    }

    public void ClearColumns() {
        var request = new ClearValuesRequest();
        _sheetService.Spreadsheets.Values.Clear(request, SpreadsheetId, SpreadsheetName).Execute();
    }

    public Dictionary<string, decimal> ReadQuotes(
        string instrumentSymbolColumn,
        string quoteColumn) {

        var quotes = new Dictionary<string, decimal>();

        // Define a range that will get all rows with data in the spreadsheet
        string range = $"{SpreadsheetName}!{instrumentSymbolColumn}1:{quoteColumn}";

        var request = _sheetService.Spreadsheets.Values.Get(SpreadsheetId, range);
        ValueRange response = request.Execute();

        foreach (IList<object> row in response.Values) {
            if (row.Count != 2)
                continue;
            string instrumentSymbol = row[0]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(instrumentSymbol))
                continue;
            if (!decimal.TryParse(row[1].ToString(), out decimal perSharePrice))
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

    private void WriteToRange(
        string column,
        List<IList<object>> values,
        SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum valueType) {

        // Set the range of cells to update
        string range = $"{SpreadsheetName}!{column}1:{column}{values.Count}";

        // Create the ValueRange object
        var valueRange = new ValueRange { Values = values };

        // Execute the update request
        var request = _sheetService.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
        request.ValueInputOption = valueType;
        _ = request.Execute();
    }

    private void EnsureConfigurationIsValid() {
        if (string.IsNullOrEmpty(CredentialKeyFileName))
            LogAndThrow(nameof(CredentialKeyFileName));
        if (string.IsNullOrEmpty(GoogleApplicationName))
            LogAndThrow(nameof(GoogleApplicationName));
        if (string.IsNullOrEmpty(SpreadsheetId))
            LogAndThrow(nameof(SpreadsheetId));
        if (string.IsNullOrEmpty(SpreadsheetName))
            LogAndThrow(nameof(SpreadsheetName));

        // Local helper functions
        void LogAndThrow(string paramName) {
            _logger.LogError("{Parameter} is not set", paramName);
            throw new ArgumentException($"{paramName} is not set", paramName);
        }
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
