using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Erp;

/// <summary>
/// One fiscal year of the ERP's books as CivicBudget needs them: monthly activity by account, open
/// encumbrances, and each fund's cash. This is the contract for actuals the way <see cref="ErpChart"/>
/// is for the chart; a file and an API both produce it, and <see cref="ActualsSyncService"/> is the
/// only thing that reads it. Codes are the ERP's segments, so the sync matches them against the
/// chart the same way the chart sync does.
/// </summary>
/// <param name="ThroughPeriod">The last fiscal month the figures include; 12 means the year is closed.</param>
public sealed record ErpActuals(
    int FiscalYear,
    int ThroughPeriod,
    IReadOnlyList<ErpActivity> Activity,
    IReadOnlyList<ErpOpenEncumbrance> Encumbrances,
    IReadOnlyList<ErpCash> Cash);

/// <summary>Receipts (revenue accounts) or spending (expenditure accounts) on one account in one fiscal month.</summary>
public sealed record ErpActivity(string FundCode, string? DepartmentCode, string ObjectCode, int Period, decimal Amount);

/// <summary>What is committed on one account and not yet spent.</summary>
public sealed record ErpOpenEncumbrance(string FundCode, string? DepartmentCode, string ObjectCode, decimal Amount);

/// <summary>A fund's cash balance.</summary>
public sealed record ErpCash(string FundCode, decimal Amount);

/// <summary>Which of the ERP's entities a request is for. VIP hosts many governments; the slug stands in for its entity id.</summary>
public sealed record ErpEntity(Guid GovernmentId, string Slug, string Name, int FiscalYearStartMonth, AccountNumberFormat NumberFormat);

/// <summary>Reads an actuals export the Fiscal Officer downloaded from the ERP and uploads here.</summary>
public interface IErpActualsFileSource
{
    /// <summary>A short name for the sync log ("VIP actuals export").</summary>
    string Name { get; }

    /// <summary>Reads CSV or XLSX; full account numbers are split with the government's own format.</summary>
    Result<ErpActuals> Read(string fileName, Stream content, AccountNumberFormat format);
}

/// <summary>
/// Asks the ERP for a year's actuals directly. Registered only where a connection is configured;
/// the demo registers a simulated VIP. When none is registered the page offers the file upload alone.
/// </summary>
public interface IErpActualsApi
{
    /// <summary>What the page and the sync log call it ("VIP (simulated)").</summary>
    string Name { get; }

    Task<Result<ErpActuals>> FetchAsync(ErpEntity entity, int fiscalYear, CancellationToken ct = default);
}
