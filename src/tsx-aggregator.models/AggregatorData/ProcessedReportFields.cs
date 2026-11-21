using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace tsx_aggregator.models;

/// <summary>
/// The fields read back from a serialized <see cref="CompanyReport"/> (the processed report JSON stored in
/// processed_instrument_reports) to build the gRPC reply and, from it, a <see cref="CompanyFullDetailReport"/>.
/// Each value is rounded to a whole number, matching the int64 fields of the gRPC reply. The nullable fields are null
/// when the report holds a JSON null (the source data didn't have the figure); reports written before W1 hold numbers.
/// </summary>
public sealed record ProcessedReportFields(
    long CurNumShares,
    long? CurTotalShareholdersEquity,
    long CurGoodwill,
    long CurIntangibles,
    long CurLongTermDebt,
    long? CurDividendsPaid,
    long? CurAdjustedRetainedEarnings,
    long? OldestRetainedEarnings,
    long? CurBookValue,
    long? AverageNetCashFlow,
    long? AverageOwnerEarnings,
    bool CurRetainedEarningsEstimated = false,
    bool OldestRetainedEarningsEstimated = false,
    long? RoeCashFlowNumerator = null,
    long? RoeCashFlowEquity = null,
    long? RoeOwnerEarningsNumerator = null,
    long? RoeOwnerEarningsEquity = null) {

    /// <summary>
    /// Reads the fields from a serialized processed report.
    /// </summary>
    /// <returns>False if any field is missing or not a number.</returns>
    /// <exception cref="JsonException">The report is not valid JSON.</exception>
    /// <exception cref="OverflowException">A field does not fit in a long.</exception>
    public static bool TryParse(string serializedReport, [NotNullWhen(true)] out ProcessedReportFields? fields) {
        using var doc = JsonDocument.Parse(serializedReport);
        JsonElement root = doc.RootElement;

        if (!GetDataPoint(root, "CurNumShares", out long curNumShares)
            || !GetNullableDataPoint(root, "CurTotalShareholdersEquity", out long? curTotalShareholdersEquity)
            || !GetDataPoint(root, "CurGoodwill", out long curGoodwill)
            || !GetDataPoint(root, "CurIntangibles", out long curIntangibles)
            || !GetDataPoint(root, "CurLongTermDebt", out long curLongTermDebt)
            || !GetNullableDataPoint(root, "CurDividendsPaid", out long? curDividendsPaid)
            || !GetNullableDataPoint(root, "CurAdjustedRetainedEarnings", out long? curAdjustedRetainedEarnings)
            || !GetNullableDataPoint(root, "OldestRetainedEarnings", out long? oldestRetainedEarnings)
            || !GetNullableDataPoint(root, "CurBookValue", out long? curBookValue)
            || !GetNullableDataPoint(root, "AverageNetCashFlow", out long? averageNetCashFlow)
            || !GetNullableDataPoint(root, "AverageOwnerEarnings", out long? averageOwnerEarnings)) {
            fields = null;
            return false;
        }

        // ROE inputs (B11). Reports written before B11 don't have them: use the old formula, average / current equity.
        long? roeCashFlowNumerator = averageNetCashFlow, roeCashFlowEquity = curTotalShareholdersEquity;
        long? roeOwnerEarningsNumerator = averageOwnerEarnings, roeOwnerEarningsEquity = curTotalShareholdersEquity;
        if (root.TryGetProperty("RoeCashFlowEquity", out _)
            && (!GetNullableDataPoint(root, "RoeCashFlowNumerator", out roeCashFlowNumerator)
                || !GetNullableDataPoint(root, "RoeCashFlowEquity", out roeCashFlowEquity)
                || !GetNullableDataPoint(root, "RoeOwnerEarningsNumerator", out roeOwnerEarningsNumerator)
                || !GetNullableDataPoint(root, "RoeOwnerEarningsEquity", out roeOwnerEarningsEquity))) {
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
            averageOwnerEarnings,
            // Optional: reports written before decision 23 don't have the flags.
            GetFlag(root, "CurRetainedEarningsEstimated"),
            GetFlag(root, "OldestRetainedEarningsEstimated"),
            roeCashFlowNumerator,
            roeCashFlowEquity,
            roeOwnerEarningsNumerator,
            roeOwnerEarningsEquity);
        return true;
    }

    private static bool GetFlag(JsonElement root, string fieldName) =>
        root.TryGetProperty(fieldName, out JsonElement jsonVal) && jsonVal.ValueKind == JsonValueKind.True;

    /// <summary>Like <see cref="GetDataPoint"/>, but a JSON null reads as null instead of failing.</summary>
    private static bool GetNullableDataPoint(JsonElement root, string fieldName, out long? retVal) {
        if (root.TryGetProperty(fieldName, out JsonElement jsonVal) && jsonVal.ValueKind == JsonValueKind.Null) {
            retVal = null;
            return true;
        }

        bool ok = GetDataPoint(root, fieldName, out long value);
        retVal = ok ? value : null;
        return ok;
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
