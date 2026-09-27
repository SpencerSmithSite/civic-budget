using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Erp;

/// <summary>
/// One month of real activity on one account, as the ERP's ledger reports it: what was received
/// on a revenue account or spent on an expenditure account in that fiscal period. CivicBudget keeps
/// no ledger of its own; these rows are a copy of the ERP's, replaced wholesale for a fiscal year
/// every time that year is synced (see <see cref="ActualsSync"/>). They are not audited row by row
/// for the same reason: the ERP is the book of record, and the sync log says who brought them in.
/// </summary>
/// <remarks>
/// A month can be negative (a refund larger than the month's receipts), so unlike a budget line
/// the amount is not forced positive. Totals shown against a budget are sums of these rows.
/// </remarks>
public sealed class ErpActual : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }

    /// <summary>The fiscal year's label year (FY2026 is 2026), as <c>FiscalYear.Year</c> is.</summary>
    public int FiscalYear { get; private set; }

    /// <summary>1 is the first month of the fiscal year, so July for a July-start township.</summary>
    public int Period { get; private set; }

    public Guid FundId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }

    public ErpActual(Guid governmentId, int fiscalYear, int period, Guid fundId, Guid? departmentId, Guid accountId, decimal amount)
    {
        Guard.Against(period is < 1 or > 12, "A fiscal period is a month from 1 to 12.");
        Guard.Against(!Money.IsStorable(amount), Money.TooLargeMessage);
        GovernmentId = governmentId;
        FiscalYear = fiscalYear;
        Period = period;
        FundId = fundId;
        DepartmentId = departmentId;
        AccountId = accountId;
        Amount = Money.Round(amount);
    }

    private ErpActual()
    {
    }
}

/// <summary>
/// Money committed on an account (purchase orders and blanket certificates) but not yet spent, as of
/// the sync's as-of date. For a closed year that date is the last day of the year, which makes
/// these the encumbrances carried into the next year: the figure Ohio's certificate of resources
/// subtracts from cash.
/// </summary>
public sealed class ErpEncumbrance : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public int FiscalYear { get; private set; }
    public Guid FundId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }

    public ErpEncumbrance(Guid governmentId, int fiscalYear, Guid fundId, Guid? departmentId, Guid accountId, decimal amount)
    {
        Guard.Against(!Money.IsStorable(amount), Money.TooLargeMessage);
        GovernmentId = governmentId;
        FiscalYear = fiscalYear;
        FundId = fundId;
        DepartmentId = departmentId;
        AccountId = accountId;
        Amount = Money.Round(amount);
    }

    private ErpEncumbrance()
    {
    }
}

/// <summary>
/// A fund's cash balance in the ERP's cash journal as of the sync's as-of date. At year end, cash
/// less carried encumbrances is the unencumbered balance a new budget starts from.
/// </summary>
public sealed class ErpFundCash : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public int FiscalYear { get; private set; }
    public Guid FundId { get; private set; }

    /// <summary>Can be negative: an overdrawn fund is exactly what a finance director needs to see.</summary>
    public decimal Amount { get; private set; }

    public ErpFundCash(Guid governmentId, int fiscalYear, Guid fundId, decimal amount)
    {
        Guard.Against(!Money.IsStorable(amount), Money.TooLargeMessage);
        GovernmentId = governmentId;
        FiscalYear = fiscalYear;
        FundId = fundId;
        Amount = Money.Round(amount);
    }

    private ErpFundCash()
    {
    }
}
