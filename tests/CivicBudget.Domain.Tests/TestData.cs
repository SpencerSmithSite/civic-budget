using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Domain.Tests;

/// <summary>Builders for the handful of entities most tests need. Keeps the tests about the rule, not the setup.</summary>
internal static class TestData
{
    public static readonly Guid GovernmentId = Guid.CreateVersion7();
    public static readonly Guid OtherGovernmentId = Guid.CreateVersion7();

    public static Government Government(int fiscalYearStartMonth = 1, AppropriationLimitMode mode = AppropriationLimitMode.Block) =>
        new("Village of Maple Ridge", GovernmentType.Village, "OH", fiscalYearStartMonth, "maple-ridge-oh", mode);

    public static Fund GeneralFund(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "1000", "General", FundCategory.General);

    public static Fund StreetFund(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "2011", "Street Construction, Maintenance & Repair", FundCategory.SpecialRevenue);

    public static Department Police(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "110", "Police");

    public static Department Streets(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "620", "Streets & Service");

    public static Account Salaries(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "5100", "Salaries", AccountType.Expenditure, ReportingCategory.PersonalServices);

    public static Account Supplies(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "5400", "Supplies", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials);

    public static Account PropertyTax(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "4110", "Real Estate Taxes", AccountType.Revenue, ReportingCategory.Taxes);

    public static Account TransferIn(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "4900", "Transfers In", AccountType.TransferIn, ReportingCategory.Transfers);

    public static Account TransferOut(Guid? governmentId = null) =>
        new(governmentId ?? GovernmentId, "5900", "Transfers Out", AccountType.TransferOut, ReportingCategory.Transfers);

    public static FiscalYear FiscalYear2027() => new(GovernmentId, 2027, 1);

    public static BudgetVersion DraftVersion() => BudgetVersion.CreateOriginal(GovernmentId, FiscalYear2027().Id);

    public static BudgetVersion AdoptedVersion()
    {
        BudgetVersion version = DraftVersion();
        version.Propose();
        version.Adopt("2026-14", "user-fd", new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero));
        return version;
    }
}
