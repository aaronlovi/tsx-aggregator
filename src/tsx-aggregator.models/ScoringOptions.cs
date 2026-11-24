using System.ComponentModel.DataAnnotations;

namespace tsx_aggregator.models;

/// <summary>
/// Scoring settings, in the "Scoring" configuration section of tsx-aggregator (and tsx-ops, with the same keys).
/// </summary>
public class ScoringOptions {
    public const string Scoring = "Scoring";

    /// <summary>A company in the top list scores at least this many passes (14 of 16 since C16, decision 28).</summary>
    public const int TopScoreCutoff = 14;

    /// <summary>A company whose newest annual or quarterly report is older than this stays off the top list (V10).</summary>
    public const int StaleReportMonths = 18;

    public const int DefaultRoeConsistencyMinYears = 4;
    public const int DefaultRoeConsistencyWindowYears = 5;
    public const int DefaultRoeConsistencyMinUsableYears = 3;

    /// <summary>Check 16: ROE (OE) over 10% in at least this many of the last <see cref="RoeConsistencyWindowYears"/> years.</summary>
    [Range(1, 20)]
    public int RoeConsistencyMinYears { get; set; } = DefaultRoeConsistencyMinYears;

    /// <summary>Check 16: how many of the most recent usable years it looks at.</summary>
    [Range(1, 20)]
    public int RoeConsistencyWindowYears { get; set; } = DefaultRoeConsistencyWindowYears;

    /// <summary>
    /// Check 16 with fewer usable years than the window: judged on those years, with the same number of misses
    /// allowed (window - min), when there are at least this many; fewer is n/a (decision 28, refined).
    /// </summary>
    [Range(1, 20)]
    public int RoeConsistencyMinUsableYears { get; set; } = DefaultRoeConsistencyMinUsableYears;

    public const decimal DefaultMaxPriceTargetReturnPct = 8M;

    /// <summary>
    /// The est. return, in percent, that the max price is set for (V3). Not the 5% of the est. return checks.
    /// </summary>
    public decimal MaxPriceTargetReturnPct { get; set; } = DefaultMaxPriceTargetReturnPct;

    /// <returns>An error message, or null when the settings are valid.</returns>
    public string? Validate() {
        if (RoeConsistencyMinYears < 1 || RoeConsistencyWindowYears < 1 || RoeConsistencyMinYears > RoeConsistencyWindowYears)
            return $"Scoring: RoeConsistencyMinYears ({RoeConsistencyMinYears}) must be between 1 and RoeConsistencyWindowYears ({RoeConsistencyWindowYears})";
        if (RoeConsistencyMinUsableYears < 1 || RoeConsistencyMinUsableYears > RoeConsistencyWindowYears)
            return $"Scoring: RoeConsistencyMinUsableYears ({RoeConsistencyMinUsableYears}) must be between 1 and RoeConsistencyWindowYears ({RoeConsistencyWindowYears})";
        if (MaxPriceTargetReturnPct <= 0 || MaxPriceTargetReturnPct > 100)
            return $"Scoring: MaxPriceTargetReturnPct ({MaxPriceTargetReturnPct}) must be more than 0 and at most 100";
        return null;
    }
}
