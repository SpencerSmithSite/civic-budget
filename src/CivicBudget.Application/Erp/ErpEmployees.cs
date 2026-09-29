using CivicBudget.Application.Common;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Erp;

/// <summary>
/// The ERP's payroll roster as CivicBudget needs it: who is employed, in which department, at what
/// pay, in which retirement system and insurance plans, and charged to which funds. This is the
/// contract for personnel the way <see cref="ErpActuals"/> is for the books; a file and an API both
/// produce it, and the personnel sync is the only thing that reads it. Plan names are matched against
/// the year's personnel settings, so an adapter for a real ERP translates its codes ("OPF-P") into
/// those names; nothing past the adapter knows the ERP's codes.
/// </summary>
/// <param name="AsOf">The date the roster describes (the payroll it was taken from).</param>
public sealed record ErpEmployees(DateOnly AsOf, IReadOnlyList<ErpEmployee> Employees);

/// <summary>One employee on the ERP's payroll.</summary>
/// <param name="EmployeeId">The ERP's employee number: how a position is found again next year.</param>
/// <param name="AnnualHours">Hours a year; null means the year's full-time hours.</param>
/// <param name="Grade">With <paramref name="Step"/>, the employee's place on a pay scale; null for a rate set by itself.</param>
/// <param name="Retirement">The retirement system by the name the year's settings give it; null for none.</param>
public sealed record ErpEmployee(
    string EmployeeId,
    string Name,
    string Title,
    string DepartmentCode,
    PayBasis Basis,
    decimal Rate,
    decimal? AnnualHours,
    DateOnly? HireDate,
    string? Grade,
    int? Step,
    string? Retirement,
    bool PicksUpEmployeeShare,
    IReadOnlyList<ErpFundShare> Funds,
    IReadOnlyList<ErpBenefit> Benefits);

/// <summary>The share of an employee's pay charged to one fund (the payroll's labor distribution).</summary>
public sealed record ErpFundShare(string FundCode, decimal Percent);

/// <summary>An insurance plan the employee is enrolled in, by the name the year's settings give it.</summary>
public sealed record ErpBenefit(string Plan, CoverageTier Tier);

/// <summary>Reads an employee export the Fiscal Officer downloaded from the ERP and uploads here.</summary>
public interface IErpEmployeeFileSource
{
    /// <summary>A short name for the sync log ("ERP employee export file").</summary>
    string Name { get; }

    Result<ErpEmployees> Read(string fileName, Stream content, DateOnly today);
}

/// <summary>
/// Asks the ERP for its current employees directly. Registered only where a connection is configured;
/// the demo registers a simulated ERP. With none, the page offers the file upload alone.
/// </summary>
public interface IErpEmployeesApi
{
    /// <summary>What the page and the sync log call it ("ERP (simulated)").</summary>
    string Name { get; }

    /// <summary>
    /// Whether this government has a connection. One adapter serves every government, and only the ones
    /// the operator configured are connected; for the rest the page offers the file alone.
    /// </summary>
    bool IsConnected(Guid governmentId);

    Task<Result<ErpEmployees>> FetchAsync(ErpEntity entity, CancellationToken ct = default);
}
