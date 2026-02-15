namespace tsx_aggregator.models;

public record CompanySummaryReport(
    string Exchange,
    string InstrumentSymbol,
    string CompanyName,
    decimal PricePerShare,
    decimal CurMarketCap,
    decimal EstimatedNextYearTotalReturnPercentage_FromCashFlow,
    decimal EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
    int OverallScore,
    decimal MaxPrice) {

    public decimal PercentageUpside {
        get {
            if (PricePerShare <= 0 || MaxPrice == -1)
                return decimal.MinValue;
            return (MaxPrice - PricePerShare) / PricePerShare * 100M;
        }
    }

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
            fullDetailReport.MaxPrice);
    }
}
