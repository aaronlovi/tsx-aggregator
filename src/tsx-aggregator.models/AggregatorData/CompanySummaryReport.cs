using System;

namespace tsx_aggregator.models;

public record CompanySummaryReport(
    string Exchange,
    string InstrumentSymbol,
    string CompanyName,
    decimal PricePerShare,
    decimal? CurMarketCap,
    decimal? EstimatedNextYearTotalReturnPercentage_FromCashFlow,
    decimal? EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
    int OverallScore,
    int NotAvailableCount,
    decimal? MaxPrice,
    decimal? PercentageUpside,
    decimal? ReturnOnEquity_FromCashFlow,
    decimal? ReturnOnEquity_FromOwnerEarnings,
    DateTimeOffset? MostRecentAnnualReportDate = null,
    DateTimeOffset? MostRecentQuarterlyReportDate = null,
    DateTimeOffset? LastUpdatedDate = null) {

    public static CompanySummaryReport FromDetailedReport(CompanyFullDetailReport fullDetailReport) {
        return new CompanySummaryReport(
            fullDetailReport.Exchange,
            fullDetailReport.InstrumentSymbol,
            fullDetailReport.CompanyName,
            fullDetailReport.PricePerShare,
            fullDetailReport.CurMarketCap,
            fullDetailReport.EstimatedNextYearTotalReturnPercentage_FromCashFlow,
            fullDetailReport.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
            fullDetailReport.OverallScore,
            fullDetailReport.NotAvailableCount,
            fullDetailReport.MaxPrice,
            fullDetailReport.PercentageUpside,
            fullDetailReport.ReturnOnEquity_FromCashFlow,
            fullDetailReport.ReturnOnEquity_FromOwnerEarnings,
            fullDetailReport.MostRecentAnnualReportDate,
            fullDetailReport.MostRecentQuarterlyReportDate,
            fullDetailReport.LastUpdatedDate);
    }
}
