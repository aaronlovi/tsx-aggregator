using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using tsx_aggregator.models;

namespace tsx_ops.Snapshots;

/// <summary>
/// An instrument the API would list, with the frozen price used for its snapshot row.
/// </summary>
internal sealed record SnapshotInstrument(
    long InstrumentId,
    string Exchange,
    string CompanySymbol,
    string CompanyName,
    string InstrumentSymbol,
    string InstrumentName,
    decimal PricePerShare,
    long NumShares,
    int NumAnnualCashFlowReports);

/// <summary>
/// Turns the fields read back from a processed report into one snapshot row, building the
/// <see cref="CompanyFullDetailReport"/> with the same arguments as CompaniesController.GetCompany.
/// </summary>
internal static class SnapshotRowBuilder {
    public const string StatusOk = "ok";
    public const string StatusInvalid = "invalid";

    public const int NumChecks = 15;

    public static readonly IReadOnlyList<string> CheckColumns =
        Enumerable.Range(1, NumChecks).Select(i => $"c{i:00}").ToArray();

    public static readonly IReadOnlyList<string> Columns = [
        Snapshot.InstrumentIdColumn,
        Snapshot.SymbolColumn,
        "company_name",
        Snapshot.StatusColumn,
        "price_per_share",
        "num_shares",
        "cur_total_shareholders_equity",
        "cur_book_value",
        "cur_long_term_debt",
        "average_net_cash_flow",
        "average_owner_earnings",
        "cur_dividends_paid",
        "cur_adjusted_retained_earnings",
        "oldest_retained_earnings",
        "num_annual_cash_flow_reports",
        "debt_to_equity",
        "price_to_book",
        "debt_to_book",
        "roe_cf",
        "roe_oe",
        "est_return_cf_pct",
        "est_return_oe_pct",
        "max_price",
        .. CheckColumns,
        Snapshot.ScoreColumn,
        "na_count",
    ];

    /// <summary>
    /// Builds a row for an instrument whose processed report could not be read (the API would drop it).
    /// </summary>
    public static Dictionary<string, string> BuildUnreadable(SnapshotInstrument instrument, string status) {
        Dictionary<string, string> row = Columns.ToDictionary(c => c, _ => string.Empty, StringComparer.Ordinal);
        row[Snapshot.InstrumentIdColumn] = Format(instrument.InstrumentId);
        row[Snapshot.SymbolColumn] = instrument.InstrumentSymbol;
        row["company_name"] = instrument.CompanyName;
        row[Snapshot.StatusColumn] = status;
        row["price_per_share"] = Format(instrument.PricePerShare);
        return row;
    }

    public static Dictionary<string, string> Build(SnapshotInstrument instrument, ProcessedReportFields fields) {
        var report = new CompanyFullDetailReport(
            exchange: instrument.Exchange,
            companySymbol: instrument.CompanySymbol,
            instrumentSymbol: instrument.InstrumentSymbol,
            companyName: instrument.CompanyName,
            instrumentName: instrument.InstrumentName,
            pricePerShare: instrument.PricePerShare,
            curLongTermDebt: fields.CurLongTermDebt,
            curTotalShareholdersEquity: fields.CurTotalShareholdersEquity,
            curBookValue: fields.CurBookValue,
            curNumShares: fields.CurNumShares,
            averageNetCashFlow: fields.AverageNetCashFlow,
            averageOwnerEarnings: fields.AverageOwnerEarnings,
            curDividendsPaid: fields.CurDividendsPaid,
            curAdjustedRetainedEarnings: fields.CurAdjustedRetainedEarnings,
            oldestRetainedEarnings: fields.OldestRetainedEarnings,
            numAnnualProcessedCashFlowReports: instrument.NumAnnualCashFlowReports);

        // Same order as CompanyFullDetailReport.OverallScore.
        bool[] checks = [
            report.DoesPassCheck_DebtToEquitySmallEnough,
            report.DoesPassCheck_BookValueBigEnough,
            report.DoesPassCheck_PriceToBookSmallEnough,
            report.DoesPassCheck_AvgCashFlow_Positive,
            report.DoesPassCheck_AvgOwnerEarningsPositive,
            report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough,
            report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_BigEnough,
            report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_NotTooBig,
            report.DoesPassCheck_EstNextYeartotalReturn_OwnerEarnings_NotTooBig,
            report.DoesPassCheck_DebtToBookRatioSmallEnough,
            report.DoesPassCheck_RetainedEarningsPositive,
            report.DoesPassCheck_IsHistoryLongEnough,
            report.DidAdjustedRetainedEarningsIncrease,
            report.DoesPassCheck_ROE_FromCashFlow_BigEnough,
            report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough,
        ];

        int passCount = checks.Count(c => c);
        if (passCount != report.OverallScore) {
            throw new InvalidOperationException(
                $"Snapshot check list is out of sync with CompanyFullDetailReport.OverallScore for {instrument.InstrumentSymbol}: "
                + $"{passCount} checks passed but OverallScore is {report.OverallScore}");
        }

        var row = new Dictionary<string, string>(Columns.Count, StringComparer.Ordinal) {
            [Snapshot.InstrumentIdColumn] = Format(instrument.InstrumentId),
            [Snapshot.SymbolColumn] = instrument.InstrumentSymbol,
            ["company_name"] = instrument.CompanyName,
            [Snapshot.StatusColumn] = StatusOk,
            ["price_per_share"] = Format(instrument.PricePerShare),
            ["num_shares"] = Format(fields.CurNumShares),
            ["cur_total_shareholders_equity"] = Format(fields.CurTotalShareholdersEquity),
            ["cur_book_value"] = Format(fields.CurBookValue),
            ["cur_long_term_debt"] = Format(fields.CurLongTermDebt),
            ["average_net_cash_flow"] = Format(fields.AverageNetCashFlow),
            ["average_owner_earnings"] = Format(fields.AverageOwnerEarnings),
            ["cur_dividends_paid"] = Format(fields.CurDividendsPaid),
            ["cur_adjusted_retained_earnings"] = Format(fields.CurAdjustedRetainedEarnings),
            ["oldest_retained_earnings"] = Format(fields.OldestRetainedEarnings),
            ["num_annual_cash_flow_reports"] = Format(instrument.NumAnnualCashFlowReports),
            ["debt_to_equity"] = Format(report.DebtToEquityRatio),
            ["price_to_book"] = Format(report.CurPriceToBookRatio),
            ["debt_to_book"] = Format(report.LongTermDebtToBookRatio),
            ["roe_cf"] = Format(report.ReturnOnEquity_FromCashFlow),
            ["roe_oe"] = Format(report.ReturnOnEquity_FromOwnerEarnings),
            ["est_return_cf_pct"] = Format(report.EstimatedNextYearTotalReturnPercentage_FromCashFlow),
            ["est_return_oe_pct"] = Format(report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings),
            ["max_price"] = Format(report.MaxPrice),
            [Snapshot.ScoreColumn] = Format(report.OverallScore),
            ["na_count"] = "0",
        };

        for (int i = 0; i < checks.Length; i++)
            row[CheckColumns[i]] = checks[i] ? "P" : "F";

        return row;
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
