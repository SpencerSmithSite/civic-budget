namespace CivicBudget.Domain.Erp;

/// <summary>Who owns the chart of accounts for a government.</summary>
public enum ChartSource
{
    /// <summary>Maintained here, on the setup screens. The default for a government with no ERP feed.</summary>
    Local = 1,

    /// <summary>Received from the parent ERP by sync. Setup screens are read-only; changes come from the next sync.</summary>
    Erp = 2,
}
