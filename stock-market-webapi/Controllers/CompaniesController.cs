using Microsoft.AspNetCore.Mvc;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using static tsx_aggregator.Services.StockDataService;

namespace stock_market_webapi.Controllers;

[ApiController]
public class CompaniesController : Controller {

    private readonly StockDataServiceClient _client;

    public CompaniesController(StockDataServiceClient client)
    {
        _client = client;
    }

    [HttpGet("companies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanyFullDetailReport>>> GetCompanies() {
        GetStocksDataReply reply = await _client.GetStocksDataAsync(new GetStocksDataRequest() { Exchange = "TSX" });
        if (!reply.Success)
            return BadRequest("abc");

        var output = new List<CompanyFullDetailReport>();
        foreach (GetStocksDataReplyItem item in reply.StocksData) {
            output.Add(new CompanyFullDetailReport(
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
        output.Sort((a, b) => {
            int scoreCompare = b.OverallScore.CompareTo(a.OverallScore);
            if (scoreCompare != 0)
                return scoreCompare;

            decimal aAvgReturn = (a.EstimatedNextYearTotalReturnPercentage_FromCashFlow + a.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
            decimal bAvgReturn = (b.EstimatedNextYearTotalReturnPercentage_FromCashFlow + b.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
            return bAvgReturn.CompareTo(aAvgReturn);
        });

        // Keep only the top 30 companies
        output = output.Take(30).ToList();

        return Ok(output);
    }

    [HttpGet("companies/sort/{dataPoint}")]
    public ActionResult<IEnumerable<CompanyFullDetailReport>> GetCompaniesSortedBy(string dataPoint) {
        // Retrieve and return the list of companies sorted by the specified data point
        throw new NotImplementedException("NYI");
    }

    [HttpGet("companies/{id}")]
    public ActionResult<CompanyFullDetailReport> GetCompany(int id) {
        // Retrieve and return the company with the specified ID from your data source
        throw new NotImplementedException("NYI");
    }
}
