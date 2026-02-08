using System.Collections.Generic;

namespace tsx_aggregator.models;

public class TsxCompanyData {
    public TsxCompanyData() {
        AnnualRawCashFlowReports = [];
        AnnualRawIncomeStatements = [];
        AnnualRawBalanceSheets = [];
        QuarterlyRawCashFlowReports = [];
        QuarterlyRawIncomeStatements = [];
        QuarterlyRawBalanceSheets = [];
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

    /// <summary>
    /// Clears all data to allow reuse for retry attempts.
    /// </summary>
    public void Clear() {
        Symbol = string.Empty;
        Name = string.Empty;
        Exchange = string.Empty;
        PricePerShare = 0;
        CurNumShares = 0;
        AnnualRawCashFlowReports.Clear();
        AnnualRawIncomeStatements.Clear();
        AnnualRawBalanceSheets.Clear();
        QuarterlyRawCashFlowReports.Clear();
        QuarterlyRawIncomeStatements.Clear();
        QuarterlyRawBalanceSheets.Clear();
    }
}
