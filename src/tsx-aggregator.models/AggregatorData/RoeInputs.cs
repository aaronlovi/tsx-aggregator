using System.Collections.Generic;

namespace tsx_aggregator.models;

/// <summary>
/// The return-on-equity inputs of a <see cref="CompanyFullDetailReport"/>, from <see cref="CompanyReport"/>: the
/// numerator and equity sums over matched annual years (B11, decisions 8 and 24) and the ROE (OE) of each usable year,
/// most recent first (C16). <see cref="OwnerEarningsByYear"/> is null when the stored report predates C16, which makes
/// check 16 n/a; every caller says so explicitly.
/// </summary>
public sealed record RoeInputs(
    decimal? CashFlowNumerator,
    decimal? CashFlowEquity,
    decimal? OwnerEarningsNumerator,
    decimal? OwnerEarningsEquity,
    IReadOnlyList<AnnualReturnOnEquity>? OwnerEarningsByYear) {

    /// <summary>The ROE inputs read back from a stored processed report.</summary>
    public static RoeInputs From(ProcessedReportFields fields) => new(
        CashFlowNumerator: fields.RoeCashFlowNumerator,
        CashFlowEquity: fields.RoeCashFlowEquity,
        OwnerEarningsNumerator: fields.RoeOwnerEarningsNumerator,
        OwnerEarningsEquity: fields.RoeOwnerEarningsEquity,
        OwnerEarningsByYear: fields.RoeOwnerEarningsByYear);
}
