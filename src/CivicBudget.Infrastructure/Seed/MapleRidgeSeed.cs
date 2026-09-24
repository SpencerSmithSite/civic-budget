using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// The Village of Maple Ridge, Ohio, is a fictional village of about 4,500 people. Fund numbers follow
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
            "Pays for day-to-day village services (police, administration, parks, and zoning), mostly from income and property taxes."),
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
        new(governmentId, "715", "Council & Mayor", "The elected legislative and executive offices of the village."),
        new(governmentId, "725", "Finance", "The Fiscal Officer's department: accounting, payroll, budgeting, and the annual audit."),
        new(governmentId, "110", "Police", "Full-time police department providing 24-hour patrol and Mayor's Court support."),
        new(governmentId, "620", "Streets & Service", "Street maintenance, snow removal, street lighting, and the village garage."),
        new(governmentId, "310", "Parks & Recreation", "Maintains Maple Ridge Community Park, the pool, and seasonal programs."),
        new(governmentId, "410", "Building & Zoning", "Permits, inspections, and zoning enforcement."),
        new(governmentId, "530", "Water Utility", "Operates the water treatment plant and distribution system."),
        new(governmentId, "540", "Sewer Utility", "Operates the wastewater treatment plant and collection system."),
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
        new(governmentId, "5510", "Capital Outlay - Equipment", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
        new(governmentId, "5520", "Capital Outlay - Infrastructure", AccountType.Expenditure, ReportingCategory.CapitalOutlay),
        new(governmentId, "5610", "Debt Service - Principal", AccountType.Expenditure, ReportingCategory.DebtService),
        new(governmentId, "5620", "Debt Service - Interest", AccountType.Expenditure, ReportingCategory.DebtService),
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
        new("1000", "310", "4320", 15_000m),      // shelter and pool fees: revenue attributed to a department
        new("1000", null, "4410", 32_000m),
        new("1000", null, "4510", 18_000m),
        new("1000", null, "4610", 12_000m),

        new("1000", "715", "5110", 68_000m),
        new("1000", "715", "5210", 9_520m),
        new("1000", "715", "5220", 986m),
        new("1000", "715", "5310", 45_000m),
        new("1000", "715", "5410", 6_000m),

        new("1000", "725", "5110", 92_000m),
        new("1000", "725", "5210", 12_880m),
        new("1000", "725", "5220", 1_334m),
        new("1000", "725", "5230", 24_000m),
        new("1000", "725", "5310", 38_000m),
        new("1000", "725", "5410", 4_500m),

        new("1000", "110", "5110", 520_000m),
        new("1000", "110", "5120", 38_000m),
        new("1000", "110", "5210", 100_000m),
        new("1000", "110", "5220", 8_100m),
        new("1000", "110", "5230", 96_000m),
        new("1000", "110", "5240", 14_000m),
        new("1000", "110", "5310", 62_000m),
        new("1000", "110", "5320", 11_000m),
        new("1000", "110", "5410", 15_000m),
        new("1000", "110", "5420", 28_000m),
        new("1000", "110", "5510", 45_000m, Proposed2027: 52_000m),   // replacement cruiser

        new("1000", "620", "5110", 60_000m),
        new("1000", "620", "5210", 8_400m),
        new("1000", "620", "5220", 870m),
        new("1000", "620", "5230", 18_000m),
        new("1000", "620", "5310", 20_000m),
        new("1000", "620", "5320", 42_000m),
        new("1000", "620", "5410", 9_000m),

        new("1000", "310", "5110", 35_000m),
        new("1000", "310", "5210", 4_900m),
        new("1000", "310", "5220", 508m),
        new("1000", "310", "5310", 12_000m),
        new("1000", "310", "5320", 6_500m),
        new("1000", "310", "5410", 8_000m),
        new("1000", "310", "5510", 12_000m),

        new("1000", "410", "5110", 41_000m),
        new("1000", "410", "5210", 5_740m),
        new("1000", "410", "5220", 595m),
        new("1000", "410", "5310", 15_000m),
        new("1000", "410", "5410", 2_500m),

        new("1000", null, "5910", 100_000m),     // transfer to Capital Projects

        // ---- 2011 Street Construction, Maintenance & Repair ------------------------------------
        new("2011", null, "4220", 245_000m),
        new("2011", null, "4230", 78_000m),
        new("2011", null, "4510", 3_000m),

        new("2011", "620", "5110", 145_000m),
        new("2011", "620", "5210", 20_300m),
        new("2011", "620", "5220", 2_100m),
        new("2011", "620", "5230", 42_000m),
        new("2011", "620", "5240", 6_000m),
        new("2011", "620", "5310", 30_000m),
        new("2011", "620", "5320", 4_000m),
        new("2011", "620", "5410", 38_000m),
        new("2011", "620", "5420", 16_000m),
        new("2011", "620", "5520", 20_000m, Proposed2027: 120_000m), // resurfacing program; pushes FY2027 over the limit

        // ---- 4901 Capital Projects -------------------------------------------------------------
        new("4901", null, "4910", 100_000m),     // from General
        new("4901", null, "4510", 4_000m),

        new("4901", "620", "5310", 15_000m),      // engineering
        new("4901", "620", "5520", 90_000m),
        new("4901", "310", "5510", 25_000m),      // playground equipment

        // ---- 5101 Water ----------------------------------------------------------------------
        new("5101", "530", "4320", 720_000m),     // water sales
        new("5101", "530", "4330", 18_000m),
        new("5101", null, "4510", 5_000m),

        new("5101", "530", "5110", 165_000m),
        new("5101", "530", "5210", 23_100m),
        new("5101", "530", "5220", 2_400m),
        new("5101", "530", "5230", 48_000m),
        new("5101", "530", "5240", 5_500m),
        new("5101", "530", "5310", 55_000m),
        new("5101", "530", "5320", 68_000m),
        new("5101", "530", "5410", 42_000m),
        new("5101", "530", "5520", 85_000m),
        new("5101", "530", "5610", 60_000m),
        new("5101", "530", "5620", 22_000m),

        // ---- 5201 Sewer ----------------------------------------------------------------------
        new("5201", "540", "4320", 640_000m),     // sewer charges
        new("5201", null, "4510", 4_000m),

        new("5201", "540", "5110", 140_000m),
        new("5201", "540", "5210", 19_600m),
        new("5201", "540", "5220", 2_030m),
        new("5201", "540", "5230", 40_000m),
        new("5201", "540", "5240", 5_000m),
        new("5201", "540", "5310", 48_000m),
        new("5201", "540", "5320", 75_000m),
        new("5201", "540", "5410", 35_000m),
        new("5201", "540", "5520", 70_000m),
        new("5201", "540", "5610", 95_000m),
        new("5201", "540", "5620", 41_000m),
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
        ("1000", "110", "5120", 53_000m),   // overtime: two officers on extended leave
        ("1000", "110", "5310", 71_000m),   // county dispatch contract increase
    ];

    public const string Amendment1Reason =
        "Supplemental appropriation for police overtime coverage and the county dispatch contract increase effective July 1.";

    /// <summary>
    /// Department narratives: the "budget message" each department writes with the request. Set on
    /// FY2026 (so the published amendment carries them to the portal) and again on the FY2027 draft.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Narratives { get; } = new Dictionary<string, string>
    {
        ["110"] = "The department requests funding for eight sworn officers and one part-time clerk, the same staffing as this year. "
                + "Overtime covers court appearances and two festival weekends. Capital outlay replaces the 2017 cruiser, which has "
                + "passed 140,000 miles; the county dispatch contract increases 6% under the new three-year agreement.",
        ["620"] = "Streets & Service proposes the same crew of four with one seasonal hire for summer paving. Salt and fuel are "
                + "budgeted at this year's actual usage plus 5%. The capital request funds the Maple Street resurfacing project "
                + "(Elm to Route 42) from the Street fund's gasoline tax balance.",
        ["310"] = "Parks & Recreation asks to keep the pool open one additional week in August and to replace the community park "
                + "playground surface, which failed its spring inspection. Program revenue from swim lessons offsets about a third "
                + "of pool operating costs.",
        ["530"] = "The Water Utility request holds operating costs flat except chemicals, which follow the supplier's contract "
                + "escalator. Debt service on the treatment plant note continues on schedule; no rate change is proposed for 2027.",
    };

    /// <summary>What the fiscal officer wrote when sending the FY2027 Parks request back.</summary>
    public const string ParksReturnNote =
        "Council asked that the playground surface come from the Capital Projects fund, not the General Fund. Please move the "
        + "capital outlay line and resubmit.";
}
