namespace tsx_aggregator.models;

public record CompanySummaryReport(
    string Exchange,
    string InstrumentSymbol,
    string CompanyName,
    decimal PricePerShare,
    decimal CurMarketCap,
    decimal EstimatedNextYearTotalReturnPercentage_FromCashFlow,
    decimal EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
    int OverallScore) {

    public static CompanySummaryReport FromDetailedReport(CompanyFullDetailReport fullDetailReport) {
        return new CompanySummaryReport(
            fullDetailReport.Exchange,
            fullDetailReport.InstrumentSymbol,
            fullDetailReport.CompanyName,
            fullDetailReport.PricePerShare,
            fullDetailReport.CurMarketCap,
            fullDetailReport.EstimatedNextYearTotalReturnPercentage_FromCashFlow,
            fullDetailReport.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
            fullDetailReport.OverallScore);
    }
}
