using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Tests;

public class AccountTests
{
    public static TheoryData<AccountType, ReportingCategory> ValidPairs()
    {
        var data = new TheoryData<AccountType, ReportingCategory>();
        foreach (AccountType type in Enum.GetValues<AccountType>())
        {
            foreach (ReportingCategory category in ReportingCategoryRules.CategoriesFor(type))
            {
                data.Add(type, category);
            }
        }

        return data;
    }

    public static TheoryData<AccountType, ReportingCategory> InvalidPairs()
    {
        var data = new TheoryData<AccountType, ReportingCategory>();
        foreach (AccountType type in Enum.GetValues<AccountType>())
        {
            foreach (ReportingCategory category in Enum.GetValues<ReportingCategory>().Except(ReportingCategoryRules.CategoriesFor(type)))
            {
                data.Add(type, category);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidPairs))]
    public void Accepts_categories_that_match_the_account_type(AccountType type, ReportingCategory category)
    {
        var account = new Account(TestData.GovernmentId, "0001", "Test", type, category);
        Assert.Equal(category, account.Category);
    }

    [Theory]
    [MemberData(nameof(InvalidPairs))]
    public void Rejects_categories_that_do_not_match_the_account_type(AccountType type, ReportingCategory category) =>
        Assert.Throws<DomainException>(() => new Account(TestData.GovernmentId, "0001", "Test", type, category));

    [Fact]
    public void Every_category_is_valid_for_exactly_one_kind_of_account()
    {
        // Transfers is shared by TransferIn and TransferOut; every other category belongs to one type.
        foreach (ReportingCategory category in Enum.GetValues<ReportingCategory>())
        {
            int matchingTypes = Enum.GetValues<AccountType>().Count(t => ReportingCategoryRules.IsValidFor(category, t));
            int expected = category == ReportingCategory.Transfers ? 2 : 1;
            Assert.Equal(expected, matchingTypes);
        }
    }

    [Theory]
    [InlineData(AccountType.Revenue, true, false)]
    [InlineData(AccountType.TransferIn, true, false)]
    [InlineData(AccountType.Expenditure, false, true)]
    [InlineData(AccountType.TransferOut, false, true)]
    public void Account_types_are_either_resources_or_appropriations(AccountType type, bool isResource, bool isAppropriation)
    {
        Assert.Equal(isResource, type.IsResource());
        Assert.Equal(isAppropriation, type.IsAppropriation());
    }

    [Theory]
    [InlineData(FundCategory.General, FundGroup.Governmental)]
    [InlineData(FundCategory.SpecialRevenue, FundGroup.Governmental)]
    [InlineData(FundCategory.DebtService, FundGroup.Governmental)]
    [InlineData(FundCategory.CapitalProjects, FundGroup.Governmental)]
    [InlineData(FundCategory.Permanent, FundGroup.Governmental)]
    [InlineData(FundCategory.Enterprise, FundGroup.Proprietary)]
    [InlineData(FundCategory.InternalService, FundGroup.Proprietary)]
    [InlineData(FundCategory.Fiduciary, FundGroup.Fiduciary)]
    public void Fund_categories_roll_up_to_gasb_groups(FundCategory category, FundGroup group) =>
        Assert.Equal(group, category.ToGroup());
}
