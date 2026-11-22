using System;
using System.Collections.Generic;
using System.Text.Json;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public class RawReportDataMap : NormalizedStringKeysHashMap<decimal> {
    public DateOnly? ReportDate { get; init; }
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// The currency the provider reports this statement in (e.g. "CAD", "USD"); null if unknown (stored before review
    /// X1). Stored in the report JSON as CURRENCY, next to REPORTDATE. Many TSX issuers report in USD while their price
    /// is in CAD.
    /// </summary>
    public string? Currency { get; set; }

    private const string CurrencyKey = "CURRENCY";

    /// <summary>
    /// True when merging this report into the stored one would change nothing: every numeric field here is in the
    /// stored report, under any letter case, with the same value. Fields only in the stored report do not count, because
    /// <see cref="MergeWith"/> keeps them; counting them rewrote the same report on every scrape (review P10).
    /// </summary>
    public bool IsEqual(JsonDocument jsonObj) {
        var stored = new Dictionary<string, decimal>();
        string? storedCurrency = null;
        foreach (JsonProperty prop in jsonObj.RootElement.EnumerateObject()) {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out decimal val))
                stored[prop.Name.ToUpperInvariant()] = val;
            else if (prop.Value.ValueKind == JsonValueKind.String && prop.Name.Equals(CurrencyKey, StringComparison.OrdinalIgnoreCase))
                storedCurrency = prop.Value.GetString();
        }

        // A known currency that is not stored (reports stored before X1), or differs, is a change.
        if (Currency is not null && !string.Equals(Currency, storedCurrency, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string key in Keys) {
            if (!stored.TryGetValue(key, out decimal storedValue) || storedValue != this[key])
                return false;
        }

        return true;
    }

    public string MergeWith(JsonDocument existingReportJson) {
        // Start with all existing fields (normalize keys to uppercase to match NormalizedStringKeysHashMap)
        var merged = new Dictionary<string, object>();
        foreach (JsonProperty prop in existingReportJson.RootElement.EnumerateObject()) {
            string normalizedKey = prop.Name.ToUpperInvariant();
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out decimal val))
                merged[normalizedKey] = val;
            else if (prop.Value.ValueKind == JsonValueKind.String)
                merged[normalizedKey] = prop.Value.GetString()!;
        }
        // Overlay new numeric fields (add new keys, overwrite changed values)
        // Keys from NormalizedStringKeysHashMap are already uppercase
        foreach (string key in Keys) {
            merged[key] = this[key]!;
        }
        // Set REPORTDATE from this report if available
        if (ReportDate is not null)
            merged["REPORTDATE"] = ReportDate.Value.ToString("yyyy-MM-dd") + "T00:00:00Z";
        if (Currency is not null)
            merged[CurrencyKey] = Currency;
        return JsonSerializer.Serialize(merged);
    }

    public string AsJsonString() {
        var map = new Dictionary<string, object>();
        foreach (string x in Keys) {            
            map.Add(x, this[x]!);
        }
        if (ReportDate is not null)
            map.Add("REPORTDATE", ReportDate.Value.ToString("yyyy-MM-dd") + "T00:00:00Z");
        if (Currency is not null)
            map.Add(CurrencyKey, Currency);
        return JsonSerializer.Serialize(map);
    }

    public static RawReportDataMap FromJsonString(string json) {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var reportData = new RawReportDataMap();
        foreach (JsonProperty prop in root.EnumerateObject()) {
            if (prop.Value.ValueKind == JsonValueKind.String && prop.Name.Equals(CurrencyKey, StringComparison.OrdinalIgnoreCase))
                reportData.Currency = prop.Value.GetString();
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;
            reportData[prop.Name] = prop.Value.GetDecimal();
        }
        return reportData;
    }
}
