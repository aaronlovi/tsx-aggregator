using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal abstract class TsxCompanyProcessorFsmInputBase { }


internal class GotResponse : TsxCompanyProcessorFsmInputBase {
    /// <summary>The scrape attempt whose page produced this response; responses from an earlier attempt are ignored.</summary>
    public int Attempt { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

internal enum TsxCompanyProcessorFsmStates {
    Invalid = 0, Initial, InProgress, InError, Done
}

internal record TsxCompanyProcessorFsmOutputBase { }
internal record TsxCompanyProcessorFsmOutputInProgress : TsxCompanyProcessorFsmOutputBase { }
internal record TsxCompanyProcessorFsmOutputCompanyRawFinancials(TsxCompanyData CompanyReport) : TsxCompanyProcessorFsmOutputBase { }
internal record TsxCompanyProcessorFsmOutputEncounteredError(string ErrMsg) : TsxCompanyProcessorFsmOutputBase { }

internal class TsxCompanyProcessorFsm {
    private readonly ILogger _logger;
    private readonly InstrumentDto _instrumentDto;
    private bool _gotQuarterlyFigures;
    private bool _gotAnnualFigures;
    private bool _gotEnhancedQuotes;
    private readonly TsxCompanyData _companyData;
    private TsxCompanyProcessorFsmStates _state;

    public TsxCompanyProcessorFsm(InstrumentDto instrumentDto, ILogger<TsxCompanyProcessorFsm> logger) {
        _logger = logger;
        _instrumentDto = instrumentDto;
        _companyData = new();
        _state = TsxCompanyProcessorFsmStates.Initial;
    }

    public bool IsDone => _state.IsCompleted();

    /// <summary>A response for this instrument could not be used; the scrape attempt cannot succeed.</summary>
    public bool IsInError => _state == TsxCompanyProcessorFsmStates.InError;
    public bool GotAllData => _gotQuarterlyFigures && _gotAnnualFigures && _gotEnhancedQuotes;

    /// <summary>
    /// Resets the FSM state for retry attempts.
    /// </summary>
    public void Reset() {
        _gotQuarterlyFigures = false;
        _gotAnnualFigures = false;
        _gotEnhancedQuotes = false;
        _companyData.Clear();
        _state = TsxCompanyProcessorFsmStates.Initial;
    }

    public IList<TsxCompanyProcessorFsmOutputBase> Update(TsxCompanyProcessorFsmInputBase input) {
        var outputList = new List<TsxCompanyProcessorFsmOutputBase>();

        if (IsDone) {
            outputList.Add(new TsxCompanyProcessorFsmOutputEncounteredError("Update in invalid state: " + _state));
            return outputList;
        }

        switch (input) {
            case GotResponse gr: {
                ProcessGotResponse(gr, outputList);
                break;
            }
            default: 
                
                break;
        }

        return outputList;
    }

    private void ProcessGotResponse(GotResponse gr, List<TsxCompanyProcessorFsmOutputBase> outputList) {
        // Only the query string goes to ParseQueryString: given the whole URL, the first parameter's key would be
        // "https://...json?symbol", and a response whose symbol came first would be rejected.
        int queryStart = gr.Url.IndexOf('?', StringComparison.Ordinal);
        NameValueCollection nameValuePairs = HttpUtility.ParseQueryString(queryStart < 0 ? string.Empty : gr.Url[(queryStart + 1)..]);

        var isEnhancedQuotesResponse = gr.Url.Includes("getEnhancedQuotes.json?");
        var isFinancialReportsResponse = gr.Url.Includes("getFinancialsEnhancedBySymbol.json?");
        string? reportType = nameValuePairs.Get("reportType");
        var isQuarterlyResponse = isFinancialReportsResponse && reportType == "Q";
        var isAnnualResponse = isFinancialReportsResponse && reportType == "A";

        if (isEnhancedQuotesResponse) {
            if (!nameValuePairs.AllKeys.Contains("symbols")) {
                string errMsg = $"Url {gr.Url} does not contain 'symbols'";
                _logger.LogWarning("Update error: {Error}", errMsg);
                outputList.Add(new TsxCompanyProcessorFsmOutputEncounteredError(errMsg));
                return;
            }
            if (!DoesSymbolMatch("symbols", nameValuePairs))
                return;
        }

        if (isFinancialReportsResponse) {
            if (!nameValuePairs.AllKeys.Contains("symbol")) {
                string errMsg = $"Url {gr.Url} does not contain 'symbol'";
                _logger.LogWarning("Update error: {Error}", errMsg);
                outputList.Add(new TsxCompanyProcessorFsmOutputEncounteredError(errMsg));
                return;
            }
            if (!DoesSymbolMatch("symbol", nameValuePairs))
                return;
        }

        int outputsBefore = outputList.Count;
        if (isEnhancedQuotesResponse)
            ProcessEnhancedQuote(gr.Text, outputList);
        if (isQuarterlyResponse)
            ProcessQuarterlyFigures(gr.Text, outputList);
        if (isAnnualResponse)
            ProcessAnnualFigures(gr.Text, outputList);

        // A response for this instrument that cannot be used means this attempt cannot succeed: say so now, rather
        // than leave the caller waiting for its timeout. (Responses for other symbols returned above, harmlessly.)
        if (outputList.Skip(outputsBefore).OfType<TsxCompanyProcessorFsmOutputEncounteredError>().Any()) {
            _state = TsxCompanyProcessorFsmStates.InError;
            return;
        }

        if (GotAllData) {
            _logger.LogInformation("Update: Instrument {Instrument} complete", _instrumentDto);
            outputList.Add(new TsxCompanyProcessorFsmOutputCompanyRawFinancials(_companyData));
            _state = TsxCompanyProcessorFsmStates.Done;
        } else {
            _logger.LogInformation("Update: Waiting on other responses");
            outputList.Add(new TsxCompanyProcessorFsmOutputInProgress());
            _state = TsxCompanyProcessorFsmStates.InProgress;
        }

        // Local helper methods

        bool DoesSymbolMatch(string key, NameValueCollection nvPairs) {
            var symbol = nvPairs.Get(key) ?? string.Empty;
            if (!symbol.Equals(_instrumentDto.InstrumentSymbol, StringComparison.Ordinal)) {
                string errMsg = $"Symbol {symbol} does not match current instrument: {_instrumentDto}";
                _logger.LogWarning("Update error: {Error}", errMsg);
                outputList.Add(new TsxCompanyProcessorFsmOutputEncounteredError(errMsg));
                return false;
            }
            return true;
        }
    }

    private void ProcessEnhancedQuote(string text, List<TsxCompanyProcessorFsmOutputBase> fsmOutputs) {
        _logger.LogInformation("ProcessEnhancedQuote - Instrument:{Instrument}", _instrumentDto);
        _logger.LogDebug("Body: {Text}", text);

        try {
            using JsonDocument jsonData = JsonDocument.Parse(text);
            JsonElement root = jsonData.RootElement;
            if (!root.TryGetProperty("results", out JsonElement results)
                || !results.TryGetProperty("quote", out JsonElement resultsQuote)
                || resultsQuote.ValueKind is not JsonValueKind.Array
                || resultsQuote.GetArrayLength() == 0
                || !resultsQuote.EnumerateArray().First().TryGetProperty("key", out _)) {
                _logger.LogWarning("ProcessEnhancedQuote - cannot get exchange for this instrument, aborting");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - cannot get exchange for this instrument"));
                return;
            }
            JsonElement quote = resultsQuote.EnumerateArray().First();
            if (!quote.TryGetProperty("pricedata", out JsonElement priceData)
                || !priceData.TryGetProperty("last", out JsonElement lastPriceData)
                || lastPriceData.ValueKind != JsonValueKind.Number) {
                _logger.LogWarning("ProcessEnhancedQuote - cannot get price per share data for this instrument, aborting");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - cannot get price per share data for this instrument"));
                return;
            }
            if (!quote.TryGetProperty("fundamental", out JsonElement fundamental)) {
                _logger.LogWarning("ProcessEnhancedQuote - cannot find 'fundamental' element for this instrument, aborting");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - cannot find 'fundamental' element for this instrument"));
                return;
            }
            if (!fundamental.TryGetProperty("totalsharesoutstanding", out JsonElement fundamentalTotalSharesOutstanding)
                && !fundamental.TryGetProperty("sharesoutstanding", out fundamentalTotalSharesOutstanding)) {
                _logger.LogWarning("ProcessEnhancedQuote - cannot get shares outstanding for this instrument, aborting");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - cannot get shares outstanding for this instrument"));
                return;
            }
            if (fundamentalTotalSharesOutstanding.ValueKind is not JsonValueKind.Number) {
                _logger.LogWarning("ProcessEnhancedQuote - total shares outstanding is not numeric, aborting");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - total shares outstanding is not numeric"));
                return;
            }

            _companyData.Symbol = _instrumentDto.InstrumentSymbol;
            _companyData.Name = _instrumentDto.InstrumentName;
            _companyData.Exchange = _instrumentDto.Exchange;
            _companyData.PricePerShare = lastPriceData.GetDecimal();
            _companyData.CurNumShares = fundamentalTotalSharesOutstanding.GetUInt64();
            _gotEnhancedQuotes = true;

            _logger.LogInformation("#ProcessEnhancedQuote - symbol: {InstrumentSymbol}", _instrumentDto.InstrumentSymbol);
        } catch (JsonException ex) {
            _logger.LogError(ex, "ProcessEnhancedQuote - failed to parse JSON data from response body, aborting");
            fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - failed to parse JSON data from response body"));
        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessEnhancedQuote - error, aborting");
            fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessEnhancedQuote - general error: " + ex.Message));
        }
    }

    private void ProcessQuarterlyFigures(string text, List<TsxCompanyProcessorFsmOutputBase> fsmOutputs) => 
        ProcessReports(text, Constants.ReportPeriodTypes.Quarterly, fsmOutputs);

    private void ProcessAnnualFigures(string text, List<TsxCompanyProcessorFsmOutputBase> fsmOutputs) =>
        ProcessReports(text, Constants.ReportPeriodTypes.Annual, fsmOutputs);

    private void ProcessReports(string text, Constants.ReportPeriodTypes reportPeriodType, List<TsxCompanyProcessorFsmOutputBase> fsmOutputs) {
        _logger.LogInformation("ProcessReports - Instrument:{Instrument}, Period Type:{ReportPeriodType}", _instrumentDto, reportPeriodType);
        _logger.LogDebug("Body: {Text}", text);

        try {
            using JsonDocument jsonData = JsonDocument.Parse(text);
            JsonElement root = jsonData.RootElement;
            if (!root.TryGetProperty("results", out JsonElement results)
                || !results.TryGetProperty("Company", out JsonElement companyResults)
                || !companyResults.TryGetProperty("Report", out JsonElement reports)
                || reports.ValueKind is not JsonValueKind.Array) {
                _logger.LogWarning("ProcessReports - Invalid body, skipping");
                fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessReports: Invalid body"));
                return;
            }

            foreach (var report in reports.EnumerateArray()) {
                if (!report.TryGetProperty("reportDate", out JsonElement reportDateObj)
                    || reportDateObj.ValueKind is not JsonValueKind.String) {
                    _logger.LogWarning("Missing date for report");
                    continue;
                }
                if (!DateOnly.TryParseExact(reportDateObj.GetString(), "yyyy-MM-dd", out DateOnly reportDate)) {
                    _logger.LogWarning("Report date {ReportDateStr} for report is in invalid format", reportDateObj.GetString());
                    continue;
                }

                if (!report.TryGetProperty("CashFlow", out JsonElement cashFlowReportObj)) {
                    _logger.LogWarning("Missing cash flow report for {ReportDate}", reportDate);
                    continue;
                }
                if (!report.TryGetProperty("IncomeStatement", out JsonElement incomeStatementObj)) {
                    _logger.LogWarning("Missing income statement for {ReportDate}", reportDate);
                    continue;
                }
                if (!report.TryGetProperty("BalanceSheet", out JsonElement balanceSheetObj)) {
                    _logger.LogWarning("Missing balance sheet for {ReportDate}", reportDate);
                    continue;
                }
                
                var cashFlowReport = PopulateReport(reportDate, cashFlowReportObj);
                var incomeStatement = PopulateReport(reportDate, incomeStatementObj);
                var balanceSheet = PopulateReport(reportDate, balanceSheetObj);

                if (reportPeriodType is Constants.ReportPeriodTypes.Quarterly) {
                    _companyData.QuarterlyRawCashFlowReports.Add(cashFlowReport);
                    _companyData.QuarterlyRawIncomeStatements.Add(incomeStatement);
                    _companyData.QuarterlyRawBalanceSheets.Add(balanceSheet);
                } else {
                    _companyData.AnnualRawCashFlowReports.Add(cashFlowReport);
                    _companyData.AnnualRawIncomeStatements.Add(incomeStatement);
                    _companyData.AnnualRawBalanceSheets.Add(balanceSheet);
                }
            }

            if (reportPeriodType == Constants.ReportPeriodTypes.Quarterly)
                _gotQuarterlyFigures = true;
            else
                _gotAnnualFigures = true;

            _logger.LogInformation("#ProcessReports - Instrument:{Instrument}, Period Type:{ReportPeriodType}", _instrumentDto, reportPeriodType);
        } catch (JsonException ex) {
            _logger.LogError(ex, "ProcessReports - failed to parse JSON data from response body, aborting");
            fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessReports - failed to parse JSON data from response body"));
        } catch (Exception ex) {
            _logger.LogError(ex, "ProcessReports - error, aborting");
            fsmOutputs.Add(new TsxCompanyProcessorFsmOutputEncounteredError("ProcessReports - general error: " + ex.Message));
        }

        RawReportDataMap PopulateReport(DateOnly reportDate, JsonElement reportJsonObj) {
            var report = new RawReportDataMap { ReportDate = reportDate };
            if (reportJsonObj.ValueKind != JsonValueKind.Object) {
                _logger.LogWarning("PopulateReport {ReportDate} found a report that is not an 'object'", reportDate);
                report.IsValid = false;
                return report;
            }
            foreach (JsonProperty prop in reportJsonObj.EnumerateObject()) {
                if (prop.Value.ValueKind != JsonValueKind.Number)
                    continue;
                report[prop.Name] = prop.Value.GetDecimal();
            }
            return report;
        }
    }
}
