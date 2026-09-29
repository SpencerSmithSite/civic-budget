using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Reports;

/// <summary>
/// Which optional sections a government's budget book carries. The cover, contents, message,
/// summary, fund and department pages, and the certificate are always there; these four vary by
/// government (a township with three employees has little to say in a personnel section). The
/// published book uses them, and they pre-fill the choices when someone prints one.
/// One row per government, created the first time the defaults are saved.
/// </summary>
[Audited]
public sealed class BudgetBookSettings : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }

    /// <summary>The multi-year plan: each fund's balance year by year, and the percentages behind it.</summary>
    public bool IncludeOutlook { get; private set; } = true;

    /// <summary>Personnel cost by fund and department (totals, no names or pay).</summary>
    public bool IncludePersonnel { get; private set; } = true;

    /// <summary>Every account line as an appendix. Off by default: it is most of the page count.</summary>
    public bool IncludeLineItems { get; private set; }

    public bool IncludeGlossary { get; private set; } = true;

    public BudgetBookSettings(Guid governmentId) => GovernmentId = governmentId;

    public void Set(bool outlook, bool personnel, bool lineItems, bool glossary) =>
        (IncludeOutlook, IncludePersonnel, IncludeLineItems, IncludeGlossary) = (outlook, personnel, lineItems, glossary);
}
