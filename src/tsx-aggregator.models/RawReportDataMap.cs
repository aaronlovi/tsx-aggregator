using System;
using System.Collections.Generic;
using System.Text.Json;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public class RawReportDataMap : NormalizedStringKeysHashMap<decimal> {
    public DateOnly? ReportDate { get; init; }
    public bool IsValid { get; set; } = true;

    public bool IsEqual(JsonDocument jsonObj) {
        foreach (JsonProperty prop in jsonObj.RootElement.EnumerateObject()) {
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;
            bool isNumeric = prop.Value.TryGetDecimal(out decimal val);
            if (!isNumeric)
                continue;
            if (!HasValue(prop.Name))
                return false;
            if (val != this[prop.Name])
                return false;
        }

        foreach (string key in Keys) {
            if (!jsonObj.RootElement.TryGetProperty(key, out JsonElement jsonElem))
                return false;
            if (jsonElem.ValueKind != JsonValueKind.Number)
                continue;
            var res = jsonElem.TryGetDecimal(out decimal val);
            if (!res)
                return false;
            if (val != this[key])
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
        return JsonSerializer.Serialize(merged);
    }

    public string AsJsonString() {
        var map = new Dictionary<string, object>();
        foreach (string x in Keys) {            
            map.Add(x, this[x]!);
        }
        if (ReportDate is not null)
            map.Add("REPORTDATE", ReportDate.Value.ToString("yyyy-MM-dd") + "T00:00:00Z");
        return JsonSerializer.Serialize(map);
    }

    public static RawReportDataMap FromJsonString(string json) {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var reportData = new RawReportDataMap();
        foreach (JsonProperty prop in root.EnumerateObject()) {
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;
            reportData[prop.Name] = prop.Value.GetDecimal();
        }
        return reportData;
    }
}
