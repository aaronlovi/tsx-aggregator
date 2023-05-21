using Microsoft.AspNetCore.Mvc;
using tsx_aggregator.models;

namespace stock_market_webapi.Controllers;

[ApiController]
public class CompaniesController {
    [HttpGet]
    public ActionResult<IEnumerable<CompanyFullDetailReport>> GetCompanies() {
        // Retrieve and return the list of companies from your data source
    }

    [HttpGet("sort/{dataPoint}")]
    public ActionResult<IEnumerable<CompanyFullDetailReport>> GetCompaniesSortedBy(string dataPoint) {
        // Retrieve and return the list of companies sorted by the specified data point
    }

    [HttpGet("{id}")]
    public ActionResult<CompanyFullDetailReport> GetCompany(int id) {
        // Retrieve and return the company with the specified ID from your data source
    }
}
