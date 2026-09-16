using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// The Village of Maple Ridge, Ohio — a fictional village of about 4,500 people. Fund numbers follow
/// the Ohio Auditor of State's UAN chart so they read naturally to Ohio finance staff.
/// Three fiscal years: FY2025 adopted, FY2026 adopted plus one adopted amendment, FY2027 draft
/// (with the Street fund deliberately over its appropriation limit so the validation is demonstrable).
/// </summary>
internal static class MapleRidgeSeed
{
    public const string Slug = "maple-ridge-oh";

    public static Government Government() =>
        new("Village of Maple Ridge", GovernmentType.Village, "OH", fiscalYearStartMonth: 1, Slug, AppropriationLimitMode.Block);

    public static IReadOnlyList<Fund> Funds(Guid governmentId) =>
    [
        new(governmentId, "1000", "General Fund", FundCategory.General,
            "Pays for day-to-day village services — police, administration, parks, and zoning — mostly from income and property taxes."),
        new(governmentId, "2011", "Street Construction, Maintenance & Repair", FundCategory.SpecialRevenue,
            "State gasoline tax and vehicle license fees that, by law, may only be spent on streets."),
        new(governmentId, "4901", "Capital Projects", FundCategory.CapitalProjects,
            "Money set aside for large one-time projects such as street reconstruction and park improvements."),
        new(governmentId, "5101", "Water", FundCategory.Enterprise,
            "Runs the water system like a business: water bills pay for treatment, distribution, and debt on the plant."),
        new(governmentId, "5201", "Sewer", FundCategory.Enterprise,
            "Runs the sanitary sewer system from sewer charges, including debt on the treatment plant upgrade."),
    ];

    public static IReadOnlyList<Department> Departments(Guid governmentId) =>
    [
        new(governmentId, "ADM", "Council & Mayor", "The elected legislative and executive offices of the village."),
        new(governmentId, "FIN", "Finance", "The Fiscal Officer's department: accounting, payroll, budgeting, and the annual audit."),
        new(governmentId, "PD", "Police", "Full-time police department providing 24-hour patrol and Mayor's Court support."),
        new(governmentId, "ST", "Streets & Service", "Street maintenance, snow removal, street lighting, and the village garage."),
        new(governmentId, "PR", "Parks & Recreation", "Maintains Maple Ridge Community Park, the pool, and seasonal programs."),
        new(governmentId, "BZ", "Building & Zoning", "Permits, inspections, and zoning enforcement."),
        new(governmentId, "WU", "Water Utility", "Operates the water treatment plant and distribution system."),
        new(governmentId, "SU", "Sewer Utility", "Operates the wastewater treatment plant and collection system."),
    ];

    public static IReadOnlyList<Account> Accounts(Guid governmentId) =>
    [
        // Revenue
        new(governmentId, "4110", "Real Estate Taxes", AccountType.Revenue, ReportingCategory.Taxes),
        new(governmentId, "4130", "Municipal Income Tax", AccountType.Revenue, ReportingCategory.Taxes),
        new(governmentId, "4210", "Local Government Fund", AccountType.Revenue, ReportingCategory.Intergovernmental),
        new(governmentId, "4220", "Gasoline Tax", AccountType.Revenue, ReportingCategory.Intergovernmental),
        new(governmentId, "4230", "Motor Vehicle License Tax", AccountType.Revenue, ReportingCategory.Intergovernmental),
        new(governmentId, "4310", "Mayor's Court Fines", AccountType.Revenue, ReportingCategory.FinesAndForfeitures),
        new(governmentId, "4320", "Charges for Services", AccountType.Revenue, ReportingCategory.ChargesForServices),
        new(governmentId, "4330", "Tap-In Fees", AccountType.Revenue, ReportingCategory.ChargesForServices),
        new(governmentId, "4410", "Licenses & Permits", AccountType.Revenue, ReportingCategory.LicensesAndPermits),
        new(governmentId, "4510", "Interest Income", AccountType.Revenue, ReportingCategory.InvestmentIncome),
        new(governmentId, "4610", "Miscellaneous Revenue", AccountType.Revenue, ReportingCategory.Miscellaneous),
        new(governmentId, "4910", "Transfers In", AccountType.TransferIn, ReportingCategory.Transfers),

        // Expenditure
        new(governmentId, "5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices),
        new(governmentId, "5120", "Overtime", AccountType.Expenditure, ReportingCategory.PersonalServices),
        new(governmentId, "5210", "Retirement Contributions", AccountType.Expenditure, ReportingCategory.FringeBenefits),
        new(governmentId, "5220", "Medicare", AccountType.Expenditure, ReportingCategory.FringeBenefits),
        new(governmentId, "5230", "Health Insurance", AccountType.Expenditure, ReportingCategory.FringeBenefits),
        new(governmentId, "5240", "Workers' Compensation", AccountType.Expenditure, ReportingCategory.FringeBenefits),
        new(governmentId, "5310", "Contractual Services", AccountType.Expenditure, ReportingCategory.ContractualServices),
        new(governmentId, "5320", "Utilities", AccountType.Expenditure, ReportingCategory.ContractualServices),
        new(governmentId, "5410", "Supplies & Materials", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials),
        new(governmentId, "5420", "Fuel", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials),
        new(governmentId, "5510", "Capital Outlay – Equipment", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
        new(governmentId, "5520", "Capital Outlay – Infrastructure", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
        new(governmentId, "5610", "Debt Service – Principal", AccountType.Expenditure, ReportingCategory.DebtService),
        new(governmentId, "5620", "Debt Service – Interest", AccountType.Expenditure, ReportingCategory.DebtService),
        new(governmentId, "5710", "Other Expenses", AccountType.Expenditure, ReportingCategory.OtherExpenditure),
        new(governmentId, "5910", "Transfers Out", AccountType.TransferOut, ReportingCategory.Transfers),
    ];

    /// <summary>Every budget line, expressed by its FY2025 budget. See <see cref="SeedLine"/> for how other years derive.</summary>
    public static IReadOnlyList<SeedLine> Lines { get; } =
    [
        // ---- 1000 General Fund ---------------------------------------------------------------
        new("1000", null, "4110", 410_000m),
        new("1000", null, "4130", 1_250_000m),
        new("1000", null, "4210", 95_000m),
        new("1000", null, "4310", 48_000m),
        new("1000", "PR", "4320", 15_000m),      // shelter and pool fees — revenue attributed to a department
        new("1000", null, "4410", 32_000m),
        new("1000", null, "4510", 18_000m),
        new("1000", null, "4610", 12_000m),

        new("1000", "ADM", "5110", 68_000m),
        new("1000", "ADM", "5210", 9_520m),
        new("1000", "ADM", "5220", 986m),
        new("1000", "ADM", "5310", 45_000m),
        new("1000", "ADM", "5410", 6_000m),

        new("1000", "FIN", "5110", 92_000m),
        new("1000", "FIN", "5210", 12_880m),
        new("1000", "FIN", "5220", 1_334m),
        new("1000", "FIN", "5230", 24_000m),
        new("1000", "FIN", "5310", 38_000m),
        new("1000", "FIN", "5410", 4_500m),

        new("1000", "PD", "5110", 520_000m),
        new("1000", "PD", "5120", 38_000m),
        new("1000", "PD", "5210", 100_000m),
        new("1000", "PD", "5220", 8_100m),
        new("1000", "PD", "5230", 96_000m),
        new("1000", "PD", "5240", 14_000m),
        new("1000", "PD", "5310", 62_000m),
        new("1000", "PD", "5320", 11_000m),
        new("1000", "PD", "5410", 15_000m),
        new("1000", "PD", "5420", 28_000m),
        new("1000", "PD", "5510", 45_000m, Proposed2027: 52_000m),   // replacement cruiser

        new("1000", "ST", "5110", 60_000m),
        new("1000", "ST", "5210", 8_400m),
        new("1000", "ST", "5220", 870m),
        new("1000", "ST", "5230", 18_000m),
        new("1000", "ST", "5310", 20_000m),
        new("1000", "ST", "5320", 42_000m),
        new("1000", "ST", "5410", 9_000m),

        new("1000", "PR", "5110", 35_000m),
        new("1000", "PR", "5210", 4_900m),
        new("1000", "PR", "5220", 508m),
        new("1000", "PR", "5310", 12_000m),
        new("1000", "PR", "5320", 6_500m),
        new("1000", "PR", "5410", 8_000m),
        new("1000", "PR", "5510", 12_000m),

        new("1000", "BZ", "5110", 41_000m),
        new("1000", "BZ", "5210", 5_740m),
        new("1000", "BZ", "5220", 595m),
        new("1000", "BZ", "5310", 15_000m),
        new("1000", "BZ", "5410", 2_500m),

        new("1000", null, "5910", 100_000m),     // transfer to Capital Projects

        // ---- 2011 Street Construction, Maintenance & Repair ------------------------------------
        new("2011", null, "4220", 245_000m),
        new("2011", null, "4230", 78_000m),
        new("2011", null, "4510", 3_000m),

        new("2011", "ST", "5110", 145_000m),
        new("2011", "ST", "5210", 20_300m),
        new("2011", "ST", "5220", 2_100m),
        new("2011", "ST", "5230", 42_000m),
        new("2011", "ST", "5240", 6_000m),
        new("2011", "ST", "5310", 30_000m),
        new("2011", "ST", "5320", 4_000m),
        new("2011", "ST", "5410", 38_000m),
        new("2011", "ST", "5420", 16_000m),
        new("2011", "ST", "5520", 20_000m, Proposed2027: 120_000m), // resurfacing program — pushes FY2027 over the limit

        // ---- 4901 Capital Projects -------------------------------------------------------------
        new("4901", null, "4910", 100_000m),     // from General
        new("4901", null, "4510", 4_000m),

        new("4901", "ST", "5310", 15_000m),      // engineering
        new("4901", "ST", "5520", 90_000m),
        new("4901", "PR", "5510", 25_000m),      // playground equipment

        // ---- 5101 Water ----------------------------------------------------------------------
        new("5101", "WU", "4320", 720_000m),     // water sales
        new("5101", "WU", "4330", 18_000m),
        new("5101", null, "4510", 5_000m),

        new("5101", "WU", "5110", 165_000m),
        new("5101", "WU", "5210", 23_100m),
        new("5101", "WU", "5220", 2_400m),
        new("5101", "WU", "5230", 48_000m),
        new("5101", "WU", "5240", 5_500m),
        new("5101", "WU", "5310", 55_000m),
        new("5101", "WU", "5320", 68_000m),
        new("5101", "WU", "5410", 42_000m),
        new("5101", "WU", "5520", 85_000m),
        new("5101", "WU", "5610", 60_000m),
        new("5101", "WU", "5620", 22_000m),

        // ---- 5201 Sewer ----------------------------------------------------------------------
        new("5201", "SU", "4320", 640_000m),     // sewer charges
        new("5201", null, "4510", 4_000m),

        new("5201", "SU", "5110", 140_000m),
        new("5201", "SU", "5210", 19_600m),
        new("5201", "SU", "5220", 2_030m),
        new("5201", "SU", "5230", 40_000m),
        new("5201", "SU", "5240", 5_000m),
        new("5201", "SU", "5310", 48_000m),
        new("5201", "SU", "5320", 75_000m),
        new("5201", "SU", "5410", 35_000m),
        new("5201", "SU", "5520", 70_000m),
        new("5201", "SU", "5610", 95_000m),
        new("5201", "SU", "5620", 41_000m),
    ];

    /// <summary>Estimated unencumbered beginning balances by fund and fiscal year.</summary>
    public static IReadOnlyDictionary<(string FundCode, int Year), decimal> BeginningBalances { get; } =
        new Dictionary<(string, int), decimal>
        {
            [("1000", 2025)] = 585_000m,
            [("1000", 2026)] = 620_000m,
            [("1000", 2027)] = 655_000m,
            [("2011", 2025)] = 95_000m,
            [("2011", 2026)] = 82_000m,
            [("2011", 2027)] = 60_000m,
            [("4901", 2025)] = 210_000m,
            [("4901", 2026)] = 180_000m,
            [("4901", 2027)] = 165_000m,
            [("5101", 2025)] = 340_000m,
            [("5101", 2026)] = 372_000m,
            [("5101", 2027)] = 401_000m,
            [("5201", 2025)] = 285_000m,
            [("5201", 2026)] = 296_000m,
            [("5201", 2027)] = 310_000m,
        };

    /// <summary>FY2026 Amendment 1: a mid-year supplemental appropriation. (Fund, department, account, new amount.)</summary>
    public static IReadOnlyList<(string FundCode, string? DepartmentCode, string AccountCode, decimal NewAmount)> Amendment1Changes { get; } =
    [
        ("1000", "PD", "5120", 53_000m),   // overtime: two officers on extended leave
        ("1000", "PD", "5310", 71_000m),   // county dispatch contract increase
    ];

    public const string Amendment1Reason =
        "Supplemental appropriation for police overtime coverage and the county dispatch contract increase effective July 1.";
}
