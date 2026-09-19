using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Erp;

/// <summary>
/// The chart of accounts as the parent ERP describes it: the three code lists CivicBudget budgets
/// against, plus how that ERP writes a full account number. This is the whole contract between
/// the two systems; an ERP adapter's job is to fill it and nothing else.
/// </summary>
public sealed record ErpChart(
    IReadOnlyList<ErpFund> Funds,
    IReadOnlyList<ErpDepartment> Departments,
    IReadOnlyList<ErpObject> Objects,
    AccountNumberFormat? NumberFormat)
{
    public int Count => Funds.Count + Departments.Count + Objects.Count;
}

public sealed record ErpFund(string Code, string Name, FundCategory Category, string? Description, bool IsActive);

/// <summary>A program (UAN) or department (county/city charts): the middle segment of an account number.</summary>
public sealed record ErpDepartment(string Code, string Name, string? Description, bool IsActive);

/// <summary>An object code with the type and reporting category CivicBudget's rules need.</summary>
public sealed record ErpObject(string Code, string Name, AccountType Type, ReportingCategory Category, bool IsActive);

/// <summary>
/// Where a chart comes from. The first implementation reads a file the ERP exported; a later one
/// could call the ERP's API. Either way <see cref="ChartSyncService"/> is the only caller and sees
/// only an <see cref="ErpChart"/>, so swapping the source touches nothing else (ADR-0025).
/// </summary>
public interface IErpChartSource
{
    /// <summary>A short name for the audit trail and the sync log ("VIP export file").</summary>
    string Name { get; }
}

/// <summary>An ERP chart source that reads an exported file. The Fiscal Officer uploads it on the sync screen.</summary>
public interface IErpChartFileSource : IErpChartSource
{
    /// <summary>Reads CSV or XLSX; fails as a whole when the file is not a chart export.</summary>
    Common.Result<ErpChart> Read(string fileName, Stream content);
}
