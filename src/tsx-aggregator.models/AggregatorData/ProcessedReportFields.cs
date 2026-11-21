using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace tsx_aggregator.models;

/// <summary>
/// The fields read back from a serialized <see cref="CompanyReport"/> (the processed report JSON stored in
/// processed_instrument_reports) to build the gRPC reply and, from it, a <see cref="CompanyFullDetailReport"/>.
/// Each value is rounded to a whole number, matching the int64 fields of the gRPC reply.
/// </summary>
public sealed record ProcessedReportFields(
    long CurNumShares,
    long CurTotalShareholdersEquity,
    long CurGoodwill,
    long CurIntangibles,
    long CurLongTermDebt,
    long CurDividendsPaid,
    long CurAdjustedRetainedEarnings,
    long OldestRetainedEarnings,
    long CurBookValue,
    long AverageNetCashFlow,
    long AverageOwnerEarnings) {

    /// <summary>
    /// Reads the fields from a serialized processed report.
    /// </summary>
    /// <returns>False if any field is missing or not a number.</returns>
    /// <exception cref="JsonException">The report is not valid JSON.</exception>
    /// <exception cref="OverflowException">A field does not fit in a long (e.g. a decimal.MinValue sentinel).</exception>
    public static bool TryParse(string serializedReport, [NotNullWhen(true)] out ProcessedReportFields? fields) {
        using var doc = JsonDocument.Parse(serializedReport);
        JsonElement root = doc.RootElement;

        if (!GetDataPoint(root, "CurNumShares", out long curNumShares)
            || !GetDataPoint(root, "CurTotalShareholdersEquity", out long curTotalShareholdersEquity)
            || !GetDataPoint(root, "CurGoodwill", out long curGoodwill)
            || !GetDataPoint(root, "CurIntangibles", out long curIntangibles)
            || !GetDataPoint(root, "CurLongTermDebt", out long curLongTermDebt)
            || !GetDataPoint(root, "CurDividendsPaid", out long curDividendsPaid)
            || !GetDataPoint(root, "CurAdjustedRetainedEarnings", out long curAdjustedRetainedEarnings)
            || !GetDataPoint(root, "OldestRetainedEarnings", out long oldestRetainedEarnings)
            || !GetDataPoint(root, "CurBookValue", out long curBookValue)
            || !GetDataPoint(root, "AverageNetCashFlow", out long averageNetCashFlow)
            || !GetDataPoint(root, "AverageOwnerEarnings", out long averageOwnerEarnings)) {
            fields = null;
            return false;
        }

        fields = new ProcessedReportFields(
            curNumShares,
            curTotalShareholdersEquity,
            curGoodwill,
            curIntangibles,
            curLongTermDebt,
            curDividendsPaid,
            curAdjustedRetainedEarnings,
            oldestRetainedEarnings,
            curBookValue,
            averageNetCashFlow,
            averageOwnerEarnings);
        return true;
    }

    private static bool GetDataPoint(JsonElement root, string fieldName, out long retVal) {
        if (root.TryGetProperty(fieldName, out JsonElement jsonVal) && jsonVal.ValueKind == JsonValueKind.Number) {
            retVal = (long)Math.Round(jsonVal.GetDecimal(), 0, MidpointRounding.ToEven);
            return true;
        }

        retVal = 0;
        return false;
    }
}
