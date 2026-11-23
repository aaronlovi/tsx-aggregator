namespace tsx_aggregator.models;

internal class CashFlowItem
{
    internal CashFlowItem() { }

    internal decimal? NetIncomeFromContinuingOperations { get; set; }
    /// <summary>Operating cash flow as reported (continuing operations if given); null when the statement has none.</summary>
    internal decimal? ReportedOperatingCashFlow { get; set; }
    internal decimal GrossCashFlow { get; set; }
    /// <summary>CashDividendsPaid, positive as reported; 0 when the key is missing.</summary>
    internal decimal DividendsPaid { get; set; }
    internal decimal NetIssuanceOfDebt { get; set; }
    internal decimal NetIssuanceOfStock { get; set; }
    internal decimal NetIssuanceOfPreferredStock { get; set; }
    internal decimal ChangeInWorkingCapital { get; set; }
    /// <summary>Depreciation plus amortization, counted once (see CompanyReport.SetDepreciationAndAmortization).</summary>
    internal decimal DepreciationAndAmortization { get; set; }
    internal decimal Depletion { get; set; }
    internal decimal DeferredTax { get; set; }
    internal decimal OtherNonCashItems { get; set; }
    /// <summary>Net purchases and sales of PP&amp;E, as reported: negative for a net outflow.</summary>
    internal decimal NetPPEPurchaseAndSale { get; set; }
    /// <summary>True when the statement had any capex key. Missing capex is unknown, not zero.</summary>
    internal bool HasCapExData { get; set; }

    // Derived properties

    internal decimal NetCashFlow => GrossCashFlow - (NetIssuanceOfDebt + NetIssuanceOfStock + NetIssuanceOfPreferredStock);
    /// <summary>Total net capital expenditure, positive when the company spent more on PP&amp;E than it sold.</summary>
    internal decimal CapEx => -NetPPEPurchaseAndSale;
    /// <summary>
    /// A year with depreciation, amortization or depletion but no capex data would add the non-cash charges back
    /// without subtracting the spending they stand for, so it is left out of the owner-earnings average.
    /// </summary>
    internal bool IsUsableForOwnerEarnings => HasCapExData || DepreciationAndAmortization + Depletion <= 0;
    /// <summary>
    /// Reported operating cash flow minus capex (V1). Only a statement without operating cash flow is rebuilt from
    /// net income and the non-cash items, because the rebuild misses every line outside the keys it reads: IFRS
    /// statements add back the whole tax expense (tagged DeferredTax) and subtract taxes paid on a line it never read.
    /// </summary>
    internal decimal OwnerEarnings => ReportedOperatingCashFlow is decimal operatingCashFlow
        ? operatingCashFlow - CapEx
        : RebuiltOwnerEarnings;

    private decimal RebuiltOwnerEarnings => (NetIncomeFromContinuingOperations ?? 0)
        + DepreciationAndAmortization
        + Depletion
        + DeferredTax
        + OtherNonCashItems
        - CapEx
        + ChangeInWorkingCapital;
}
