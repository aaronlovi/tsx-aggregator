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

    public string AsJsonString() {
        var map = new Dictionary<string, object>();
        foreach (string x in Keys) {            
            map.Add(x, this[x]!);
        }
        if (ReportDate is not null)
            map.Add("REPORTDATE", ReportDate.Value.ToString("yyyy-MM-dd") + "T00:00:00Z");
        return JsonSerializer.Serialize(map);
    }
}

