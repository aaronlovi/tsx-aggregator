using System.Text.Json.Serialization;

namespace tsx_aggregator.models;

/// <summary>
/// The outcome of one scoring check (W1). NotAvailable means an input was missing from the source data; it earns no
/// point, like Fail, but the UI and the alert can tell the two apart. Serialized as a string ("Pass", "Fail",
/// "NotAvailable").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckResult>))]
public enum CheckResult {
    Pass,
    Fail,
    NotAvailable,
}
