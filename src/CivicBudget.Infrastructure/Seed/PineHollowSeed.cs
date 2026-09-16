using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Pine Hollow Township, Ohio — a deliberately tiny second tenant. It exists to prove tenant isolation
/// (its rows must never appear for Maple Ridge) and to exercise a July fiscal-year start and Warn mode.
/// </summary>
internal static class PineHollowSeed
{
    public const string Slug = "pine-hollow-twp-oh";

    public static Government Government() =>
        new("Pine Hollow Township", GovernmentType.Township, "OH", fiscalYearStartMonth: 7, Slug, AppropriationLimitMode.Warn);

    public static IReadOnlyList<Fund> Funds(Guid governmentId) =>
    [
        new(governmentId, "1000", "General Fund", FundCategory.General, "Township administration, cemetery, and zoning."),
        new(governmentId, "2031", "Gasoline Tax", FundCategory.SpecialRevenue, "State gasoline tax restricted to township roads."),
    ];

    public static IReadOnlyList<Department> Departments(Guid governmentId) =>
    [
        new(governmentId, "TR", "Trustees", "The three elected township trustees and the fiscal officer."),
        new(governmentId, "RD", "Road", "Maintains 22 miles of township roads."),
    ];

    public static IReadOnlyList<Account> Accounts(Guid governmentId) =>
    [
        new(governmentId, "4110", "Real Estate Taxes", AccountType.Revenue, ReportingCategory.Taxes),
        new(governmentId, "4220", "Gasoline Tax", AccountType.Revenue, ReportingCategory.Intergovernmental),
        new(governmentId, "4510", "Interest Income", AccountType.Revenue, ReportingCategory.InvestmentIncome),
        new(governmentId, "5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices),
        new(governmentId, "5210", "Retirement Contributions", AccountType.Expenditure, ReportingCategory.FringeBenefits),
        new(governmentId, "5310", "Contractual Services", AccountType.Expenditure, ReportingCategory.ContractualServices),
        new(governmentId, "5410", "Supplies & Materials", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials),
        new(governmentId, "5520", "Capital Outlay – Infrastructure", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
    ];

    public static IReadOnlyList<SeedLine> Lines { get; } =
    [
        new("1000", null, "4110", 168_000m),
        new("1000", null, "4510", 2_200m),
        new("1000", "TR", "5110", 54_000m),
        new("1000", "TR", "5210", 7_560m),
        new("1000", "TR", "5310", 31_000m),
        new("1000", "TR", "5410", 4_000m),

        new("2031", null, "4220", 96_000m),
        new("2031", "RD", "5110", 38_000m),
        new("2031", "RD", "5210", 5_320m),
        new("2031", "RD", "5410", 22_000m),
        new("2031", "RD", "5520", 30_000m),
    ];

    public static IReadOnlyDictionary<(string FundCode, int Year), decimal> BeginningBalances { get; } =
        new Dictionary<(string, int), decimal>
        {
            [("1000", 2026)] = 141_000m,
            [("1000", 2027)] = 152_000m,
            [("2031", 2026)] = 27_000m,
            [("2031", 2027)] = 24_000m,
        };
}
