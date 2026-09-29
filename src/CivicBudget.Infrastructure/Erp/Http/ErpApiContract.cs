using System.Text.Json;
using System.Text.Json.Serialization;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Infrastructure.Erp.Http;

// Version 1 of the ERP API as JSON: the shapes docs/partners/openapi.json describes, property for
// property (ErpApiContractTests holds the two together). They are kept apart from the Application
// contracts (ErpChart, ErpActuals, ...) on purpose: those can change with CivicBudget, while these
// are a published promise to ERP vendors and change only by adding a version. Optional fields have
// a default; every other field must be present.

public sealed record ApiChart(IReadOnlyList<ApiFund> Funds, IReadOnlyList<ApiDepartment> Departments, IReadOnlyList<ApiObject> Objects, ApiNumberFormat? NumberFormat = null);

/// <summary>How the ERP writes a full account number: the width of each segment and the character between them.</summary>
public sealed record ApiNumberFormat(int FundWidth, int DepartmentWidth, int ObjectWidth, string Separator, string DepartmentLabel);

public sealed record ApiFund(string Code, string Name, FundCategory Category, bool Active, string? Description = null);

public sealed record ApiDepartment(string Code, string Name, bool Active, string? Description = null);

public sealed record ApiObject(string Code, string Name, AccountType Type, ReportingCategory Category, bool Active);

public sealed record ApiActuals(int FiscalYear, int ThroughPeriod, IReadOnlyList<ApiActivity> Activity, IReadOnlyList<ApiEncumbrance> Encumbrances, IReadOnlyList<ApiCash> Cash);

public sealed record ApiActivity(string FundCode, string ObjectCode, int Period, decimal Amount, string? DepartmentCode = null);

public sealed record ApiEncumbrance(string FundCode, string ObjectCode, decimal Amount, string? DepartmentCode = null);

public sealed record ApiCash(string FundCode, decimal Amount);

public sealed record ApiEmployees(DateOnly AsOf, IReadOnlyList<ApiEmployee> Employees);

public sealed record ApiEmployee(
    string EmployeeId,
    string Name,
    string Title,
    string DepartmentCode,
    PayBasis Basis,
    decimal Rate,
    IReadOnlyList<ApiFundShare> Funds,
    IReadOnlyList<ApiBenefit> Benefits,
    bool PicksUpEmployeeShare = false,
    decimal? AnnualHours = null,
    DateOnly? HireDate = null,
    string? Grade = null,
    int? Step = null,
    string? Retirement = null);

public sealed record ApiFundShare(string FundCode, decimal Percent);

public sealed record ApiBenefit(string Plan, CoverageTier Tier);

public sealed record ApiBudgetJournal(Guid ExternalId, int FiscalYear, string Description, DateOnly Date, IReadOnlyList<ApiJournalLine> Lines);

public sealed record ApiJournalLine(string Account, decimal Amount);

/// <summary>The ERP's answer to a journal: posted whole with its journal number, or refused whole with the accounts it would not take.</summary>
public sealed record ApiJournalResult(bool Posted, string? JournalNumber = null, IReadOnlyList<ApiRefusedAccount>? Refused = null, string? Message = null);

public sealed record ApiRefusedAccount(string Account, string Reason);

/// <summary>An error body in the standard problem format (RFC 9457); CivicBudget shows the detail, or else the title.</summary>
public sealed record ApiProblem(string? Title = null, string? Detail = null);

/// <summary>The JSON rules both sides use, and the translation between the published shapes and CivicBudget's own contracts.</summary>
public static class ErpApiContract
{
    public const string Version = "v1";

    /// <summary>
    /// camelCase names and enums by name, as the OpenAPI file shows them. Missing required fields and
    /// nulls where a field may not be null are errors, so a half-filled answer is refused rather than
    /// read as zeros. Fields CivicBudget does not know are ignored, so an ERP can add to its answers.
    /// An empty optional field is left out rather than written as null.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ErpChart ToContract(ApiChart chart) => new(
        [.. chart.Funds.Select(f => new ErpFund(f.Code, f.Name, f.Category, f.Description, f.Active))],
        [.. chart.Departments.Select(d => new ErpDepartment(d.Code, d.Name, d.Description, d.Active))],
        [.. chart.Objects.Select(o => new ErpObject(o.Code, o.Name, o.Type, o.Category, o.Active))],
        chart.NumberFormat is { } f ? new AccountNumberFormat(f.FundWidth, f.DepartmentWidth, f.ObjectWidth, f.Separator, f.DepartmentLabel) : null);

    public static ApiChart FromContract(ErpChart chart) => new(
        [.. chart.Funds.Select(f => new ApiFund(f.Code, f.Name, f.Category, f.IsActive, f.Description))],
        [.. chart.Departments.Select(d => new ApiDepartment(d.Code, d.Name, d.IsActive, d.Description))],
        [.. chart.Objects.Select(o => new ApiObject(o.Code, o.Name, o.Type, o.Category, o.IsActive))],
        chart.NumberFormat is { } f ? new ApiNumberFormat(f.FundWidth, f.DepartmentWidth, f.ObjectWidth, f.Separator, f.DepartmentLabel) : null);

    public static ErpActuals ToContract(ApiActuals actuals) => new(
        actuals.FiscalYear, actuals.ThroughPeriod,
        [.. actuals.Activity.Select(a => new ErpActivity(a.FundCode, a.DepartmentCode, a.ObjectCode, a.Period, a.Amount))],
        [.. actuals.Encumbrances.Select(e => new ErpOpenEncumbrance(e.FundCode, e.DepartmentCode, e.ObjectCode, e.Amount))],
        [.. actuals.Cash.Select(c => new ErpCash(c.FundCode, c.Amount))]);

    public static ApiActuals FromContract(ErpActuals actuals) => new(
        actuals.FiscalYear, actuals.ThroughPeriod,
        [.. actuals.Activity.Select(a => new ApiActivity(a.FundCode, a.ObjectCode, a.Period, a.Amount, a.DepartmentCode))],
        [.. actuals.Encumbrances.Select(e => new ApiEncumbrance(e.FundCode, e.ObjectCode, e.Amount, e.DepartmentCode))],
        [.. actuals.Cash.Select(c => new ApiCash(c.FundCode, c.Amount))]);

    public static ErpEmployees ToContract(ApiEmployees roster) => new(
        roster.AsOf,
        [.. roster.Employees.Select(e => new ErpEmployee(
            e.EmployeeId, e.Name, e.Title, e.DepartmentCode, e.Basis, e.Rate, e.AnnualHours, e.HireDate, e.Grade, e.Step, e.Retirement,
            e.PicksUpEmployeeShare,
            [.. e.Funds.Select(f => new ErpFundShare(f.FundCode, f.Percent))],
            [.. e.Benefits.Select(b => new ErpBenefit(b.Plan, b.Tier))]))]);

    public static ApiEmployees FromContract(ErpEmployees roster) => new(
        roster.AsOf,
        [.. roster.Employees.Select(e => new ApiEmployee(
            e.EmployeeId, e.Name, e.Title, e.DepartmentCode, e.Basis, e.Rate,
            [.. e.Funds.Select(f => new ApiFundShare(f.FundCode, f.Percent))],
            [.. e.Benefits.Select(b => new ApiBenefit(b.Plan, b.Tier))],
            e.PicksUpEmployeeShare, e.AnnualHours, e.HireDate, e.Grade, e.Step, e.Retirement))]);

    public static ApiBudgetJournal FromContract(ErpBudgetJournal journal) => new(
        journal.ExternalId, journal.FiscalYear, journal.Description, journal.Date,
        [.. journal.Lines.Select(l => new ApiJournalLine(l.Account, l.Amount))]);

    public static ErpBudgetJournal ToContract(ApiBudgetJournal journal) => new(
        journal.ExternalId, journal.FiscalYear, journal.Description, journal.Date,
        [.. journal.Lines.Select(l => new ErpBudgetJournalLine(l.Account, l.Amount))]);

    public static ApiJournalResult FromContract(ErpJournalAnswer answer) => new(
        answer.Posted, answer.JournalNumber,
        [.. answer.RefusedAccounts.Select(r => new ApiRefusedAccount(r.Key, r.Value))],
        answer.Message);
}
