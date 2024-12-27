using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using static tsx_aggregator.Services.StockDataService;

namespace stock_market_webapi.Controllers;

[ApiController]
public class CompaniesController : Controller {

    private const int GetCompaniesMaxNumCompanies = 30;
    private const int GetMaxQuickSearchNumResults = 5;

    private readonly StockDataServiceClient _client;

    public CompaniesController(StockDataServiceClient client) => _client = client;

    [HttpGet("companies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanySummaryReport>>> GetCompanies() {
        GetStocksDataReply reply = await _client.GetStocksDataAsync(new GetStocksDataRequest() { Exchange = "TSX" });
        if (!reply.Success)
            return BadRequest("abc");

        var fullDetailReports = new List<CompanyFullDetailReport>();
        foreach (GetStocksDataReplyItem item in reply.StocksData) {
            fullDetailReports.Add(new CompanyFullDetailReport(
                exchange: item.Exchange,
                companySymbol: item.CompanySymbol,
                instrumentSymbol: item.InstrumentSymbol,
                companyName: item.CompanyName,
                instrumentName: item.InstrumentName,
                pricePerShare: item.PerSharePrice,
                curLongTermDebt: item.CurrentLongTermDebt,
                curTotalShareholdersEquity: item.CurrentTotalShareholdersEquity,
                curBookValue: item.CurrentBookValue,
                curNumShares: item.CurrentNumShares,
                averageNetCashFlow: item.AverageNetCashFlow,
                averageOwnerEarnings: item.AverageOwnerEarnings,
                curDividendsPaid: item.CurrentDividendsPaid,
                curRetainedEarnings: item.CurrentRetainedEarnings,
                oldestRetainedEarnings: item.OldestRetainedEarnings,
                numAnnualProcessedCashFlowReports: item.NumAnnualProcessedCashFlowReports));
        }

        // Sort the output list first by overall score, then by the average of total return by cash flow and total return by owner earnings
        fullDetailReports.Sort((a, b) => {
            int scoreCompare = b.OverallScore.CompareTo(a.OverallScore);
            if (scoreCompare != 0)
                return scoreCompare;

            decimal aAvgReturn = (a.EstimatedNextYearTotalReturnPercentage_FromCashFlow + a.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
            decimal bAvgReturn = (b.EstimatedNextYearTotalReturnPercentage_FromCashFlow + b.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
            return bAvgReturn.CompareTo(aAvgReturn);
        });

        // Keep only the top 'GetCompaniesMaxNumCompanies' companies, and transform to summary reports
        var summaryReports = fullDetailReports
            .Take(GetCompaniesMaxNumCompanies)
            .Select(CompanySummaryReport.FromDetailedReport)
            .ToList();

        return Ok(summaryReports);
    }

    [HttpGet("companies/{exchange}/{instrumentSymbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompanyFullDetailReport>> GetCompany(string exchange, string instrumentSymbol) {
        GetStocksDetailReply reply = await _client.GetStocksDetailAsync(new GetStocksDetailRequest() { Exchange = "TSX", InstrumentSymbol = instrumentSymbol });
        if (!reply.Success)
            return BadRequest("abc");

        var fullDetailReport = new CompanyFullDetailReport(
            exchange: reply.StockDetail.Exchange,
            companySymbol: reply.StockDetail.CompanySymbol,
            instrumentSymbol: reply.StockDetail.InstrumentSymbol,
            companyName: reply.StockDetail.CompanyName,
            instrumentName: reply.StockDetail.InstrumentName,
            pricePerShare: reply.StockDetail.PerSharePrice,
            curLongTermDebt: reply.StockDetail.CurrentLongTermDebt,
            curTotalShareholdersEquity: reply.StockDetail.CurrentTotalShareholdersEquity,
            curBookValue: reply.StockDetail.CurrentBookValue,
            curNumShares: reply.StockDetail.CurrentNumShares,
            averageNetCashFlow: reply.StockDetail.AverageNetCashFlow,
            averageOwnerEarnings: reply.StockDetail.AverageOwnerEarnings,
            curDividendsPaid: reply.StockDetail.CurrentDividendsPaid,
            curRetainedEarnings: reply.StockDetail.CurrentRetainedEarnings,
            oldestRetainedEarnings: reply.StockDetail.OldestRetainedEarnings,
            numAnnualProcessedCashFlowReports: reply.StockDetail.NumAnnualProcessedCashFlowReports);

        return Ok(fullDetailReport);
    }

    [HttpGet("companies/quicksearch/{searchTerm}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanySearchResult>>> SearchCompanies(string searchTerm) {
        GetStockSearchResultsReply reply = await _client.GetStockSearchResultsAsync(new GetStockSearchResultsRequest() { SearchTerm = searchTerm });
        if (!reply.Success)
            return BadRequest("abc");

        var searchResult = new List<CompanySearchResult>();
        foreach (var res in reply.SearchResults.Take(GetMaxQuickSearchNumResults))
            searchResult.Add(new CompanySearchResult(res.Exchange, res.CompanyName, res.InstrumentSymbol));

        return Ok(searchResult);
    }

    [HttpGet("companies/updated_raw_data_reports")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InstrumentWithConflictingRawData>> GetUpdatedRawDataReports(
        [FromQuery] string exchange, [FromQuery] int pageNumber, [FromQuery] int pageSize) {
        try {
            var request = new GetStocksWithUpdatedRawDataReportsRequest {
                Exchange = exchange,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            GetStocksWithUpdatedRawDataReportsReply response = await _client.GetStocksWithUpdatedRawDataReportsAsync(request);
            if (!response.Success)
                return BadRequest(new { error = response.ErrorMessage });

            var pagingData = new PagingData(response.TotalItems, response.PageNumber, response.PageSize);
            var instruments = new List<InstrumentWithConflictingRawData>();
            foreach (var i in response.InstrumentRawReportsWithUpdates) {
                var conflictingReports = new List<InstrumentRawReportData>();
                foreach (InstrumentWithUpdatedRawDataItem r in i.RawReportAndUpdates) {
                    var rawReport = new InstrumentRawReportData((long)r.InstrumentReportId, r.CreatedDate.ToDateTime(), r.IsCurrent, r.CheckManually, r.IgnoreReport, r.ReportJson);
                    conflictingReports.Add(rawReport);
                }
                instruments.Add(new InstrumentWithConflictingRawData(
                    (long)i.InstrumentId,
                    i.Exchange,
                    i.CompanySymbol,
                    i.InstrumentSymbol,
                    i.CompanyName,
                    i.InstrumentName,
                    (int)i.ReportType,
                    (int)i.ReportPeriodType,
                    i.ReportDate.ToDateOnly(),
                    conflictingReports));
            }
            var reply = new InstrumentsWithConflictingRawData(pagingData, instruments);

            return Ok(reply);
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpPost("companies/ignore_raw_report/{instrumentId}/{instrumentReportIdToKeep}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> IgnoreRawReport(
        ulong instrumentId, ulong instrumentReportIdToKeep, [FromBody] List<ulong> instrumentReportIdsToIgnore) {
        try {
            if (!ModelState.IsValid || instrumentReportIdsToIgnore == null || !instrumentReportIdsToIgnore.Any())
                return BadRequest(ModelState);

            var request = new IgnoreRawDataReportRequest() {
                InstrumentId = instrumentId,
                InstrumentReportIdToKeep = instrumentReportIdToKeep,
                InstrumentReportIdsToIgnore = { instrumentReportIdsToIgnore }
            };

            StockDataServiceReply reply = await _client.IgnoreRawDataReportAsync(request);
            if (!reply.Success)
                return UnprocessableEntity(new { error = reply.ErrorMessage });

            return Ok();
        } catch (Exception) {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "An unexpected error occurred." });
        }
    }
}
