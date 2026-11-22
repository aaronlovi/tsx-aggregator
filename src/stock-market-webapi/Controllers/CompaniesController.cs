using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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

    /// <summary>How long a call to tsx-aggregator may take before the page gets an error instead of waiting forever.</summary>
    public static readonly TimeSpan AggregatorCallTimeout = TimeSpan.FromSeconds(30);

    public CompaniesController(StockDataServiceClient client) => _client = client;

    private static DateTime AggregatorDeadline() => DateTime.UtcNow + AggregatorCallTimeout;

    // Review R80: unchecked, 0 or negative values returned page 1 or an empty page claiming about 2.1 billion pages.
    private const string PagingError = "pageNumber and pageSize must be at least 1";

    [HttpGet("companies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanySummaryReport>>> GetCompanies(CancellationToken ct = default) {
        Result<List<CompanyFullDetailReport>> loaded = await LoadReportsAsync(ct);
        if (loaded.Data is not { } fullDetailReports)
            return BadRequest(new { error = loaded.ErrMsg });

        // Sort the output list first by overall score, then by the average of total return by cash flow and total return by owner earnings
        fullDetailReports.Sort(ByScoreThenAverageReturn(descending: true));

        // Keep only top-score companies (14 of 16 since C16), and transform to summary reports
        var summaryReports = fullDetailReports
            .Where(r => r.OverallScore >= ScoringOptions.TopScoreCutoff)
            .Select(CompanySummaryReport.FromDetailedReport)
            .ToList();

        return Ok(summaryReports);
    }

    [HttpGet("companies/bottom")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanySummaryReport>>> GetBottomCompanies(CancellationToken ct = default) {
        Result<List<CompanyFullDetailReport>> loaded = await LoadReportsAsync(ct);
        if (loaded.Data is not { } fullDetailReports)
            return BadRequest(new { error = loaded.ErrMsg });

        // Sort the output list first by overall score ascending, then by the average of total return ascending
        fullDetailReports.Sort(ByScoreThenAverageReturn(descending: false));

        // Keep only the bottom 'GetCompaniesMaxNumCompanies' companies, and transform to summary reports
        var summaryReports = fullDetailReports
            .Take(GetCompaniesMaxNumCompanies)
            .Select(CompanySummaryReport.FromDetailedReport)
            .ToList();

        return Ok(summaryReports);
    }

    [HttpGet("companies/all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedCompanySummaryReports>> GetAllCompanies(
        [FromQuery] int pageNumber, [FromQuery] int pageSize, CancellationToken ct = default) {
        if (pageNumber < 1 || pageSize < 1)
            return BadRequest(PagingError);

        Result<List<CompanyFullDetailReport>> loaded = await LoadReportsAsync(ct);
        if (loaded.Data is not { } fullDetailReports)
            return BadRequest(new { error = loaded.ErrMsg });

        // Sort descending by overall score, then by average return
        fullDetailReports.Sort(ByScoreThenAverageReturn(descending: true));

        var pagingData = new PagingData(fullDetailReports.Count, pageNumber, pageSize);
        var summaryReports = fullDetailReports
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(CompanySummaryReport.FromDetailedReport)
            .ToList();

        return Ok(new PagedCompanySummaryReports(pagingData, summaryReports));
    }

    [HttpGet("companies/{exchange}/{instrumentSymbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompanyFullDetailReport>> GetCompany(string exchange, string instrumentSymbol, CancellationToken ct = default) {
        GetStocksDetailReply reply = await _client.GetStocksDetailAsync(new GetStocksDetailRequest() { Exchange = exchange.ToUpperInvariant(), InstrumentSymbol = instrumentSymbol }, deadline: AggregatorDeadline(), cancellationToken: ct);
        if (!reply.Success || reply.StockDetail is null)
            return NotFound(new { error = $"No data available for {instrumentSymbol} on {exchange}" });

        var fullDetailReport = CompanyFullDetailReport.FromReplyItem(reply.StockDetail);

        return Ok(fullDetailReport);
    }

    [HttpGet("companies/quicksearch/{searchTerm}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CompanySearchResult>>> SearchCompanies(string searchTerm, CancellationToken ct = default) {
        GetStockSearchResultsReply reply = await _client.GetStockSearchResultsAsync(new GetStockSearchResultsRequest() { SearchTerm = searchTerm }, deadline: AggregatorDeadline(), cancellationToken: ct);
        if (!reply.Success)
            return BadRequest(new { error = reply.ErrorMessage });

        var searchResult = new List<CompanySearchResult>();
        foreach (var res in reply.SearchResults.Take(GetMaxQuickSearchNumResults))
            searchResult.Add(new CompanySearchResult(res.Exchange, res.CompanyName, res.InstrumentSymbol));

        return Ok(searchResult);
    }

    [HttpGet("companies/missing_data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedInstrumentInfoReports>> GetMissingDataCompanies(
        [FromQuery] string exchange, [FromQuery] int pageNumber, [FromQuery] int pageSize, CancellationToken ct = default) {
        if (pageNumber < 1 || pageSize < 1)
            return BadRequest(PagingError);

        try {
            var request = new GetInstrumentsWithNoRawReportsRequest {
                Exchange = exchange,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            GetInstrumentsWithNoRawReportsReply response = await _client.GetInstrumentsWithNoRawReportsAsync(request, deadline: AggregatorDeadline(), cancellationToken: ct);
            if (!response.Success)
                return BadRequest(new { error = response.ErrorMessage });

            var pagingData = new PagingData(response.TotalItems, response.PageNumber, response.PageSize);
            var instruments = new List<InstrumentInfo>();
            foreach (var i in response.Instruments) {
                instruments.Add(new InstrumentInfo(
                    (long)i.InstrumentId,
                    i.Exchange,
                    i.CompanySymbol,
                    i.InstrumentSymbol,
                    i.CompanyName,
                    i.InstrumentName));
            }

            return Ok(new PagedInstrumentInfoReports(pagingData, instruments));
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpPost("companies/priority")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> SetPriorityCompanies([FromBody] List<string> companySymbols, CancellationToken ct = default) {
        try {
            if (companySymbols == null)
                return BadRequest(new { error = "Request body must be a JSON array of company symbols" });

            var request = new SetPriorityCompaniesRequest();
            request.CompanySymbols.AddRange(companySymbols);

            StockDataServiceReply reply = await _client.SetPriorityCompaniesAsync(request, deadline: AggregatorDeadline(), cancellationToken: ct);
            if (!reply.Success)
                return BadRequest(new { error = reply.ErrorMessage });

            return Ok();
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet("companies/priority")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<string>>> GetPriorityCompanies(CancellationToken ct = default) {
        try {
            GetPriorityCompaniesReply reply = await _client.GetPriorityCompaniesAsync(new GetPriorityCompaniesRequest(), deadline: AggregatorDeadline(), cancellationToken: ct);
            if (!reply.Success)
                return BadRequest(new { error = reply.ErrorMessage });

            return Ok(reply.CompanySymbols.ToList());
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet("companies/dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DashboardStatsResponse>> GetDashboardStats(CancellationToken ct = default) {
        try {
            GetDashboardStatsReply reply = await _client.GetDashboardStatsAsync(new GetDashboardStatsRequest(), deadline: AggregatorDeadline(), cancellationToken: ct);
            if (!reply.Success)
                return BadRequest(new { error = reply.ErrorMessage });

            var rawReportCounts = new List<RawReportCountItem>();
            foreach (DashboardRawReportCount rc in reply.RawReportCounts) {
                rawReportCounts.Add(RawReportCountItem.FromTypeAndCount(rc.ReportType, rc.Count));
            }

            var response = new DashboardStatsResponse(
                TotalActiveInstruments: reply.TotalActiveInstruments,
                TotalObsoletedInstruments: reply.TotalObsoletedInstruments,
                InstrumentsWithProcessedReports: reply.InstrumentsWithProcessedReports,
                InstrumentsWithoutProcessedReports: reply.InstrumentsWithoutProcessedReports,
                MostRecentRawIngestion: reply.MostRecentRawIngestion?.ToDateTimeOffset(),
                MostRecentAggregation: reply.MostRecentAggregation?.ToDateTimeOffset(),
                UnprocessedEventCount: reply.UnprocessedEventCount,
                RawReportCounts: rawReportCounts,
                NextFetchDirectoryTime: reply.NextFetchDirectoryTime?.ToDateTimeOffset(),
                NextFetchInstrumentDataTime: reply.NextFetchInstrumentDataTime?.ToDateTimeOffset(),
                NextFetchQuotesTime: reply.NextFetchQuotesTime?.ToDateTimeOffset(),
                NextAggregatorCycleTime: reply.NextAggregatorCycleTime?.ToDateTimeOffset());

            return Ok(response);
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet("companies/dashboard/aggregates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DashboardAggregatesResponse>> GetDashboardAggregates(CancellationToken ct = default) {
        try {
            Result<List<CompanyFullDetailReport>> loaded = await LoadReportsAsync(ct);
            if (loaded.Data is not { } reports)
                return BadRequest(new { error = loaded.ErrMsg });

            int totalCompanies = reports.Count;
            var withPrice = reports.Where(r => r.CurMarketCap > 0).ToList();
            int companiesWithPriceData = withPrice.Count;
            int companiesWithoutPriceData = totalCompanies - companiesWithPriceData;
            int companiesPassingAllChecks = reports.Count(r => r.DoesPassCheck_Overall);

            decimal avgReturnCashFlow = 0M;
            decimal avgReturnOwnerEarnings = 0M;
            decimal medianReturnCashFlow = 0M;
            decimal medianReturnOwnerEarnings = 0M;
            decimal totalMarketCap = 0M;
            if (withPrice.Count > 0) {
                // Averages and medians skip null returns (decision 22); Average over decimal? already does.
                avgReturnCashFlow = Math.Round(withPrice.Average(r => r.EstimatedNextYearTotalReturnPercentage_FromCashFlow) ?? 0M, 2);
                avgReturnOwnerEarnings = Math.Round(withPrice.Average(r => r.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) ?? 0M, 2);
                medianReturnCashFlow = Math.Round(Median(withPrice.Select(r => r.EstimatedNextYearTotalReturnPercentage_FromCashFlow)), 2);
                medianReturnOwnerEarnings = Math.Round(Median(withPrice.Select(r => r.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings)), 2);
                totalMarketCap = withPrice.Sum(r => r.CurMarketCap) ?? 0M;
            }

            var scoreDistribution = reports
                .GroupBy(r => r.OverallScore)
                .Select(g => new ScoreDistributionItem(g.Key, g.Count()))
                .OrderByDescending(s => s.Score)
                .ToList();

            var scoreCategoryStatistics = withPrice
                .GroupBy(r => r.OverallScore)
                .Where(g => g.Key >= 1)
                .Select(g => new ScoreCategoryStats(
                    Score: g.Key,
                    Count: g.Count(),
                    SumMarketCap: Math.Round(g.Sum(r => r.CurMarketCap) ?? 0M, 2),
                    MeanMarketCap: Math.Round(g.Average(r => r.CurMarketCap) ?? 0M, 2),
                    MedianMarketCap: Math.Round(Median(g.Select(r => r.CurMarketCap)), 2),
                    MeanReturnFromCashFlow: Math.Round(g.Average(r => r.EstimatedNextYearTotalReturnPercentage_FromCashFlow) ?? 0M, 2),
                    MedianReturnFromCashFlow: Math.Round(Median(g.Select(r => r.EstimatedNextYearTotalReturnPercentage_FromCashFlow)), 2),
                    MeanReturnFromOwnerEarnings: Math.Round(g.Average(r => r.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) ?? 0M, 2),
                    MedianReturnFromOwnerEarnings: Math.Round(Median(g.Select(r => r.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings)), 2)))
                .OrderByDescending(s => s.Score)
                .ToList();

            var response = new DashboardAggregatesResponse(
                TotalCompanies: totalCompanies,
                CompaniesWithPriceData: companiesWithPriceData,
                CompaniesWithoutPriceData: companiesWithoutPriceData,
                CompaniesPassingAllChecks: companiesPassingAllChecks,
                AverageEstimatedReturn_FromCashFlow: avgReturnCashFlow,
                AverageEstimatedReturn_FromOwnerEarnings: avgReturnOwnerEarnings,
                MedianEstimatedReturn_FromCashFlow: medianReturnCashFlow,
                MedianEstimatedReturn_FromOwnerEarnings: medianReturnOwnerEarnings,
                TotalMarketCap: totalMarketCap,
                ScoreDistribution: scoreDistribution,
                ScoreCategoryStatistics: scoreCategoryStatistics);

            return Ok(response);
        } catch (Exception ex) {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Fetches every TSX company's processed report from tsx-aggregator and maps it to a <see cref="CompanyFullDetailReport"/>.
    /// A failed reply gives a failure with the aggregator's error message (Data is null); a failed call throws.
    /// </summary>
    private async Task<Result<List<CompanyFullDetailReport>>> LoadReportsAsync(CancellationToken ct) {
        GetStocksDataReply reply = await _client.GetStocksDataAsync(new GetStocksDataRequest() { Exchange = "TSX" }, deadline: AggregatorDeadline(), cancellationToken: ct);
        if (!reply.Success)
            return Result<List<CompanyFullDetailReport>>.SetFailure(reply.ErrorMessage);

        var reports = new List<CompanyFullDetailReport>(reply.StocksData.Count);
        foreach (GetStocksDataReplyItem item in reply.StocksData)
            reports.Add(CompanyFullDetailReport.FromReplyItem(item));
        return Result<List<CompanyFullDetailReport>>.SetSuccess(reports);
    }

    /// <summary>Orders by overall score, then by the average of the two est. returns (see <see cref="CompareAverageReturn"/>).</summary>
    private static Comparison<CompanyFullDetailReport> ByScoreThenAverageReturn(bool descending) => (a, b) => {
        int scoreCompare = descending ? b.OverallScore.CompareTo(a.OverallScore) : a.OverallScore.CompareTo(b.OverallScore);
        return scoreCompare != 0 ? scoreCompare : CompareAverageReturn(a, b, descending);
    };

    /// <summary>
    /// Orders by the average of the two est. returns. A company with either return null sorts last in both
    /// directions (decision 22).
    /// </summary>
    private static int CompareAverageReturn(CompanyFullDetailReport a, CompanyFullDetailReport b, bool descending) {
        decimal? aAvgReturn = (a.EstimatedNextYearTotalReturnPercentage_FromCashFlow + a.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
        decimal? bAvgReturn = (b.EstimatedNextYearTotalReturnPercentage_FromCashFlow + b.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings) / 2M;
        return (aAvgReturn, bAvgReturn) switch {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } x, { } y) => descending ? y.CompareTo(x) : x.CompareTo(y),
        };
    }

    /// <summary>Median of the non-null values; 0 when there are none.</summary>
    private static decimal Median(IEnumerable<decimal?> source) {
        var sorted = source.OfType<decimal>().OrderBy(x => x).ToList();
        int count = sorted.Count;
        if (count == 0) return 0M;
        if (count % 2 == 1) return sorted[count / 2];
        return (sorted[count / 2 - 1] + sorted[count / 2]) / 2M;
    }
}
