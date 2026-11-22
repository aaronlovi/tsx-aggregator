using System;

namespace tsx_aggregator.models;

/// <summary>
/// One year's return on equity (C16, decision 28): that year's figure over that year's average equity (B11).
/// <see cref="Roe"/> is null when the average equity is 0 or less, which never passes.
/// </summary>
public sealed record AnnualReturnOnEquity(DateOnly YearEnd, decimal? Roe);
