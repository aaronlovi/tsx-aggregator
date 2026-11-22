using System;
using System.Collections.Generic;
using System.Linq;
using tsx_aggregator.models;

namespace tsx_aggregator.Services;

/// <summary>Which instruments <c>tsx-ops requeue</c> queues, and why the others are left out (O1, decision 26).</summary>
internal sealed record RequeuePlan(
    IReadOnlyList<RequeueCandidateDto> ToQueue,
    IReadOnlyList<string> AlreadyPending,
    IReadOnlyList<string> NoRawData,
    IReadOnlyList<string> Unknown);

internal static class RequeuePlanner {
    /// <param name="candidates">Listed instruments.</param>
    /// <param name="symbols">Null for all; otherwise instrument symbols, matched case-insensitively.</param>
    public static RequeuePlan Plan(IReadOnlyList<RequeueCandidateDto> candidates, IReadOnlyCollection<string>? symbols) {
        IEnumerable<RequeueCandidateDto> selected;
        var unknown = new List<string>();
        if (symbols is null) {
            selected = candidates;
        } else {
            Dictionary<string, RequeueCandidateDto> bySymbol = candidates
                .GroupBy(c => c.InstrumentSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var picked = new List<RequeueCandidateDto>();
            foreach (string symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (bySymbol.TryGetValue(symbol, out RequeueCandidateDto? candidate))
                    picked.Add(candidate);
                else
                    unknown.Add(symbol);
            }
            selected = picked;
        }

        var toQueue = new List<RequeueCandidateDto>();
        var alreadyPending = new List<string>();
        var noRawData = new List<string>();
        foreach (RequeueCandidateDto c in selected.OrderBy(c => c.InstrumentSymbol, StringComparer.Ordinal)) {
            if (!c.HasRawReports)
                noRawData.Add(c.InstrumentSymbol);
            else if (c.HasPendingRawDataChanged)
                alreadyPending.Add(c.InstrumentSymbol);
            else
                toQueue.Add(c);
        }
        return new RequeuePlan(toQueue, alreadyPending, noRawData, unknown);
    }
}
