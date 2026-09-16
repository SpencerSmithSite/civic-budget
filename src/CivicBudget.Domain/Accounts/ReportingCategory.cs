namespace CivicBudget.Domain.Accounts;

/// <summary>
/// Reporting roll-up for an account. Expenditure categories follow common Ohio object-level
/// classifications; revenue categories follow the usual source classifications.
/// Which categories are valid for which <see cref="AccountType"/> is enforced by
/// <see cref="ReportingCategoryRules"/>.
/// </summary>
public enum ReportingCategory
{
    // Expenditure
    PersonalServices = 1,
    FringeBenefits = 2,
    ContractualServices = 3,
    SuppliesAndMaterials = 4,
    CapitalOutlay = 5,
    DebtService = 6,
    OtherExpenditure = 7,

    // Revenue
    Taxes = 20,
    Intergovernmental = 21,
    ChargesForServices = 22,
    FinesAndForfeitures = 23,
    LicensesAndPermits = 24,
    InvestmentIncome = 25,
    Miscellaneous = 26,

    // Transfers (both directions)
    Transfers = 40,
}

public static class ReportingCategoryRules
{
    public static readonly IReadOnlyList<ReportingCategory> ExpenditureCategories =
    [
        ReportingCategory.PersonalServices,
        ReportingCategory.FringeBenefits,
        ReportingCategory.ContractualServices,
        ReportingCategory.SuppliesAndMaterials,
        ReportingCategory.CapitalOutlay,
        ReportingCategory.DebtService,
        ReportingCategory.OtherExpenditure,
    ];

    public static readonly IReadOnlyList<ReportingCategory> RevenueCategories =
    [
        ReportingCategory.Taxes,
        ReportingCategory.Intergovernmental,
        ReportingCategory.ChargesForServices,
        ReportingCategory.FinesAndForfeitures,
        ReportingCategory.LicensesAndPermits,
        ReportingCategory.InvestmentIncome,
        ReportingCategory.Miscellaneous,
    ];

    public static IReadOnlyList<ReportingCategory> CategoriesFor(AccountType type) => type switch
    {
        AccountType.Expenditure => ExpenditureCategories,
        AccountType.Revenue => RevenueCategories,
        AccountType.TransferIn or AccountType.TransferOut => [ReportingCategory.Transfers],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type."),
    };

    public static bool IsValidFor(ReportingCategory category, AccountType type) =>
        CategoriesFor(type).Contains(category);
}
