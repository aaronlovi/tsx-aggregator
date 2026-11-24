using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.Services;

/// <param name="OtherReportDate">The date of the report this one was merged with.</param>
internal sealed record MergedReportPlanItem(MergedReportCandidateDto Report, DateOnly OtherReportDate, MergedReportFinding Finding) {
    public bool Reset => Finding is MergedReportFinding.WrongDate or MergedReportFinding.LeftoverFields;
}

/// <summary>What <c>tsx-ops merged-reports</c> found, and the requested symbols without a finding.</summary>
internal sealed record MergedReportPlan(IReadOnlyList<MergedReportPlanItem> Items, IReadOnlyList<string> NotFoundSymbols);

/// <summary>
/// Finds raw reports that the matching before review R31 merged with another report. It matched annual reports by calendar
/// year and the others by calendar quarter, so after a change of fiscal year end both reports of the year were merged
/// into one row on each scrape: whichever row the database returned first. Either row can hold fields of both reports,
/// and the date of either. Which fields the two share does not tell, because TMX adds fields over time, so every row of
/// such a pair that TMX still sends is reset: a row that was not merged comes back the same.
/// </summary>
internal static class MergedReportPlanner {
    private const string ReportDateKey = "REPORTDATE";
    private const string CurrencyKey = "CURRENCY";

    /// <summary>A report's JSON. <see cref="HasCurrency"/>: TMX has sent it since the currency is stored, the same deploy as R31's fix.</summary>
    private sealed record Content(DateOnly Date, bool HasCurrency);

    private sealed record Row(MergedReportCandidateDto Report, Content Content) {
        public bool IsWrongDate => Content.Date != Report.ReportDate;
    }

    /// <param name="symbols">Null or empty for all; otherwise instrument symbols, matched case-insensitively.</param>
    public static MergedReportPlan Plan(IReadOnlyList<MergedReportCandidateDto> candidates, IReadOnlyCollection<string>? symbols) {
        var rows = new List<Row>();
        foreach (MergedReportCandidateDto candidate in candidates) {
            if (Read(candidate.ReportJson) is Content content)
                rows.Add(new Row(candidate, content));
        }

        var items = new List<MergedReportPlanItem>();
        foreach (IGrouping<(long, int, int), Row> reports in rows.GroupBy(r => (r.Report.InstrumentId, r.Report.ReportType, r.Report.ReportPeriodType))) {
            foreach (Row row in reports) {
                if (Find(row, [.. reports]) is MergedReportPlanItem item)
                    items.Add(item);
            }
        }

        List<string> notFound = [];
        if (symbols is { Count: > 0 }) {
            var wanted = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            items = [.. items.Where(i => wanted.Contains(i.Report.InstrumentSymbol))];
            notFound = [.. wanted.Where(s => !items.Exists(i => s.Equals(i.Report.InstrumentSymbol, StringComparison.OrdinalIgnoreCase)))];
        }

        return new MergedReportPlan(
            [.. items.OrderBy(i => i.Report.InstrumentSymbol, StringComparer.Ordinal)
                .ThenBy(i => i.Report.ReportPeriodType)
                .ThenBy(i => i.Report.ReportType)
                .ThenBy(i => i.Report.ReportDate)],
            notFound);
    }

    private static MergedReportPlanItem? Find(Row row, IReadOnlyList<Row> sameKind) {
        if (row.IsWrongDate) {
            // Safe to drop only if the report it holds is also stored under its own date.
            bool hasOwnRow = sameKind.Any(r => r.Report.ReportDate == row.Content.Date && !r.IsWrongDate);
            return new MergedReportPlanItem(row.Report, row.Content.Date,
                hasOwnRow ? MergedReportFinding.WrongDate : MergedReportFinding.OnlyCopy);
        }

        // Reset before: this is the clean copy.
        if (row.Report.AlreadyReset)
            return null;

        Row? other = sameKind
            .Where(r => r.Report.ReportDate != row.Report.ReportDate && InSamePeriod(r, row))
            .MinBy(r => Math.Abs(r.Report.ReportDate.DayNumber - row.Report.ReportDate.DayNumber));
        return other is null
            ? null
            : new MergedReportPlanItem(row.Report, other.Report.ReportDate,
                row.Content.HasCurrency ? MergedReportFinding.LeftoverFields : MergedReportFinding.NotSent);
    }

    private static bool InSamePeriod(Row a, Row b) {
        DateOnly x = a.Report.ReportDate, y = b.Report.ReportDate;
        return x.Year == y.Year
            && (a.Report.ReportPeriodType == (int)ReportPeriodTypes.Annual || (x.Month - 1) / 3 == (y.Month - 1) / 3);
    }

    /// <summary>Null when the JSON cannot be read or has no report date.</summary>
    private static Content? Read(string json) {
        try {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
                return null;

            DateOnly? date = null;
            bool hasCurrency = false;
            foreach (JsonProperty property in doc.RootElement.EnumerateObject()) {
                string key = property.Name.ToUpperInvariant();
                if (key == ReportDateKey)
                    date = ReadDate(property.Value);
                else if (key == CurrencyKey)
                    hasCurrency = true;
            }
            return date is DateOnly d ? new Content(d, hasCurrency) : null;
        } catch (JsonException) {
            return null;
        }
    }

    private static DateOnly? ReadDate(JsonElement value) =>
        value.ValueKind is JsonValueKind.String && value.GetString() is { Length: >= 10 } text
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                ? date
                : null;
}
