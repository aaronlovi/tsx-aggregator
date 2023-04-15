using System.Collections.Generic;

namespace tsx_aggregator.models;

public class TsxCompanyData {
    public TsxCompanyData() {
        AnnualRawCashFlowReports = new List<RawReportDataMap>();
        AnnualRawIncomeStatements = new List<RawReportDataMap>();
        AnnualRawBalanceSheets = new List<RawReportDataMap>();
        QuarterlyRawCashFlowReports = new List<RawReportDataMap>();
        QuarterlyRawIncomeStatements = new List<RawReportDataMap>();
        QuarterlyRawBalanceSheets = new List<RawReportDataMap>();
    }

    public string Symbol { get; set; } = string.Empty;      // From enhanced quote
    public string Name { get; set; } = string.Empty;        // From enhanced quote
    public string Exchange { get; set; } = string.Empty;    // From enhanced quote
    public decimal PricePerShare { get; set; }              // From enhanced quote
    public ulong CurNumShares { get; set; }                 // From enhanced quote
    public IList<RawReportDataMap> AnnualRawCashFlowReports { get; init; }
    public IList<RawReportDataMap> AnnualRawIncomeStatements { get; init; }
    public IList<RawReportDataMap> AnnualRawBalanceSheets { get; init; }
    public IList<RawReportDataMap> QuarterlyRawCashFlowReports { get; init; }
    public IList<RawReportDataMap> QuarterlyRawIncomeStatements { get; init; }
    public IList<RawReportDataMap> QuarterlyRawBalanceSheets { get; init; }

}
