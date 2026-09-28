using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Pine Hollow Township, Ohio, is a deliberately tiny second tenant. It exists to prove tenant isolation
/// (its rows must never appear for Maple Ridge) and to exercise a July fiscal-year start and Warn mode.
/// </summary>
internal static class PineHollowSeed
{
    public const string Slug = "pine-hollow-twp-oh";

    public static Government Government()
    {
        var government = new Government("Pine Hollow Township", GovernmentType.Township, "OH", fiscalYearStartMonth: 7, Slug, AppropriationLimitMode.Warn);
        // A different chart layout than Maple Ridge, to show the format is per government: dotted
        // segments and "Department" for the middle one, the way many county ERPs write "1000.710.5110".
        government.SetAccountNumberFormat(new AccountNumberFormat(4, 3, 4, ".", "Department"));
        return government;
    }

    public static IReadOnlyList<Fund> Funds(Guid governmentId) =>
    [
        new(governmentId, "1000", "General Fund", FundCategory.General, "Township administration, cemetery, and zoning."),
        new(governmentId, "2031", "Gasoline Tax", FundCategory.SpecialRevenue, "State gasoline tax restricted to township roads."),
    ];

    public static IReadOnlyList<Department> Departments(Guid governmentId) =>
    [
        new(governmentId, "710", "Trustees", "The three elected township trustees and the fiscal officer."),
        new(governmentId, "610", "Road", "Maintains 22 miles of township roads."),
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
        new(governmentId, "5520", "Capital Outlay - Infrastructure", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
    ];

    public static IReadOnlyList<SeedLine> Lines { get; } =
    [
        new("1000", null, "4110", 168_000m),
        new("1000", null, "4510", 2_200m),
        new("1000", "710", "5110", 54_000m),
        new("1000", "710", "5210", 7_560m),
        new("1000", "710", "5310", 31_000m),
        new("1000", "710", "5410", 4_000m),

        new("2031", null, "4220", 96_000m),
        new("2031", "610", "5110", 38_000m),
        new("2031", "610", "5210", 5_320m),
        new("2031", "610", "5410", 22_000m),
        new("2031", "610", "5520", 30_000m),
    ];

    public static IReadOnlyDictionary<(string FundCode, int Year), decimal> BeginningBalances { get; } =
        new Dictionary<(string, int), decimal>
        {
            [("1000", 2026)] = 141_000m,
            [("1000", 2027)] = 152_000m,
            [("2031", 2026)] = 27_000m,
            [("2031", 2027)] = 24_000m,
        };

    /// <summary>
    /// The township's payroll in the simulated ERP. Pine Hollow has no personnel settings in the seed,
    /// so this is what a new government sees: set up the year from the defaults, then bring its
    /// employees in from the ERP instead of typing them.
    /// </summary>
    public static IReadOnlyList<SeedEmployee> Payroll { get; } =
    [
        new("T201", "710", "Trustee", "Pat Keller", new(2016, 1, 1), Domain.Personnel.PayBasis.Salary, 11_900m, Retirement: "OPERS"),
        new("T202", "710", "Trustee", "Lee Moreno", new(2020, 1, 1), Domain.Personnel.PayBasis.Salary, 11_900m, Retirement: "OPERS"),
        new("T203", "710", "Trustee", "Drew Hartman", new(2024, 1, 1), Domain.Personnel.PayBasis.Salary, 11_900m, Retirement: "OPERS"),
        new("T204", "710", "Fiscal Officer", "Sidney Albright", new(2017, 4, 1), Domain.Personnel.PayBasis.Salary, 16_500m, Retirement: "OPERS"),
        new("T210", "610", "Road superintendent (part-time)", "Kelly Brandt", new(2012, 5, 7), Domain.Personnel.PayBasis.Hourly, 24.50m, Hours: 1_040m,
            Retirement: "OPERS", Funds: [("2031", 100m)]),
        new("T211", "610", "Road worker (seasonal)", "Jesse Lowe", new(2023, 4, 17), Domain.Personnel.PayBasis.Hourly, 20.75m, Hours: 600m,
            Retirement: "OPERS", Funds: [("2031", 100m)]),
    ];
}
