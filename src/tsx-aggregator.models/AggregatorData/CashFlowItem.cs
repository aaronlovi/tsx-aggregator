namespace tsx_aggregator.models;

internal class CashFlowItem
{
    internal CashFlowItem() { }

    internal decimal? NetIncomeFromContinuingOperations { get; set; }
    internal decimal GrossCashFlow { get; set; }
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

    // Derived properties

    internal decimal NetCashFlow => GrossCashFlow - (NetIssuanceOfDebt + NetIssuanceOfStock + NetIssuanceOfPreferredStock);
    /// <summary>Total net capital expenditure, positive when the company spent more on PP&amp;E than it sold.</summary>
    internal decimal CapEx => -NetPPEPurchaseAndSale;
    internal decimal OwnerEarnings => (NetIncomeFromContinuingOperations ?? 0)
        + DepreciationAndAmortization
        + Depletion
        + DeferredTax
        + OtherNonCashItems
        - CapEx
        + ChangeInWorkingCapital;
}
