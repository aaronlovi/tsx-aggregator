using Microsoft.AspNetCore.Mvc;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using static tsx_aggregator.Services.StockDataService;

namespace stock_market_webapi.Controllers;

[ApiController]
public class CompaniesController : Controller {

    private const int GetCompaniesMaxNumCompanies = 30;
    private const int GetMaxQuickSearchNumResults = 5;

    private readonly StockDataServiceClient _client;

    public CompaniesController(StockDataServiceClient client) {
        _client = client;
    }

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
}
