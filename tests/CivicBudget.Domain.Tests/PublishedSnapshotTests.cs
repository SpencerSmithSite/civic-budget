using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;

namespace CivicBudget.Domain.Tests;

/// <summary>SPEC section 6: publishing freezes an adopted version; history is kept.</summary>
public class PublishedSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 5, 14, 0, 0, TimeSpan.Zero);

    private static (Government Government, FiscalYear Year, BudgetVersion Version, List<Fund> Funds) AdoptedBudget()
    {
        Government government = TestData.Government();
        var year = new FiscalYear(government.Id, 2027, 1);
        Fund general = new(government.Id, "1000", "General", FundCategory.General, "Day-to-day services.");
        Fund street = new(government.Id, "2011", "SCM&R", FundCategory.SpecialRevenue);
        var police = new Domain.Departments.Department(government.Id, "PD", "Police", "Patrol and Mayor's Court.");
        var tax = new Domain.Accounts.Account(government.Id, "4110", "Real Estate Taxes", Domain.Accounts.AccountType.Revenue, Domain.Accounts.ReportingCategory.Taxes);
        var salaries = new Domain.Accounts.Account(government.Id, "5110", "Salaries", Domain.Accounts.AccountType.Expenditure, Domain.Accounts.ReportingCategory.PersonalServices);

        BudgetVersion version = BudgetVersion.CreateOriginal(government.Id, year.Id);
        version.AddLine(general, null, tax, 500_000m, 480_000m, 490_000m);
        version.AddLine(general, police, salaries, 300_000m, 280_000m, 290_000m, "Two officers");
        version.SetBeginningBalance(general, 120_000m);
        version.SetBeginningBalance(street, 30_000m); // balance only, no lines
        version.Propose();
        version.Adopt("2026-40", "user-fd", Now.AddMonths(-1));
        return (government, year, version, [general, street]);
    }

    [Fact]
    public void Capture_copies_every_line_with_names_and_every_fund_with_its_balance()
    {
        (Government government, FiscalYear year, BudgetVersion version, List<Fund> funds) = AdoptedBudget();

        PublishedBudgetSnapshot snapshot = PublishedBudgetSnapshot.Capture(government, year, version, funds, "user-fd", "Dana", Now);

        Assert.Equal(SnapshotStatus.Active, snapshot.Status);
        Assert.Equal(2027, snapshot.FiscalYear);
        Assert.Equal("Original", snapshot.VersionLabel);
        Assert.Equal("2026-40", snapshot.ResolutionNumber);
        Assert.Equal("maple-ridge-oh", snapshot.GovernmentSlug);
        Assert.Equal(Now, snapshot.PublishedAtUtc);

        Assert.Equal(2, snapshot.Lines.Count);
        PublishedBudgetSnapshotLine salaries = snapshot.Lines.Single(l => l.AccountCode == "5110");
        Assert.Equal("PD", salaries.DepartmentCode);
        Assert.Equal("Patrol and Mayor's Court.", salaries.DepartmentDescription);
        Assert.Equal("General", salaries.FundName);
        Assert.Equal(300_000m, salaries.Amount);
        Assert.Null(snapshot.Lines.Single(l => l.AccountCode == "4110").DepartmentCode);

        Assert.Equal(2, snapshot.Funds.Count);
        Assert.Equal(120_000m, snapshot.Funds.Single(f => f.Code == "1000").BeginningBalance);
        Assert.Equal(30_000m, snapshot.Funds.Single(f => f.Code == "2011").BeginningBalance);
        Assert.Equal("Day-to-day services.", snapshot.Funds.Single(f => f.Code == "1000").Description);
    }

    [Fact]
    public void Only_adopted_versions_can_be_published()
    {
        (Government government, FiscalYear year, _, List<Fund> funds) = AdoptedBudget();
        BudgetVersion draft = BudgetVersion.CreateOriginal(government.Id, year.Id);

        Assert.Throws<DomainException>(() => PublishedBudgetSnapshot.Capture(government, year, draft, funds, "u", "U", Now));
        draft.Propose();
        Assert.Throws<DomainException>(() => PublishedBudgetSnapshot.Capture(government, year, draft, funds, "u", "U", Now));
    }

    [Fact]
    public void Capture_refuses_a_missing_fund_rather_than_publishing_a_partial_snapshot()
    {
        (Government government, FiscalYear year, BudgetVersion version, List<Fund> funds) = AdoptedBudget();
        Assert.Throws<DomainException>(() => PublishedBudgetSnapshot.Capture(government, year, version, [funds[0]], "u", "U", Now));
    }

    [Fact]
    public void Unpublish_keeps_the_row_and_records_who_and_when()
    {
        (Government government, FiscalYear year, BudgetVersion version, List<Fund> funds) = AdoptedBudget();
        PublishedBudgetSnapshot snapshot = PublishedBudgetSnapshot.Capture(government, year, version, funds, "user-fd", "Dana", Now);

        snapshot.Unpublish("user-fd", Now.AddDays(1));

        Assert.Equal(SnapshotStatus.Unpublished, snapshot.Status);
        Assert.False(snapshot.IsActive);
        Assert.Equal(Now.AddDays(1), snapshot.StatusChangedAtUtc);
        Assert.Equal(2, snapshot.Lines.Count); // nothing deleted
        Assert.Throws<DomainException>(() => snapshot.Unpublish("user-fd", Now.AddDays(2)));
    }

    [Fact]
    public void A_newer_snapshot_of_the_same_year_supersedes_the_active_one()
    {
        (Government government, FiscalYear year, BudgetVersion version, List<Fund> funds) = AdoptedBudget();
        PublishedBudgetSnapshot first = PublishedBudgetSnapshot.Capture(government, year, version, funds, "u", "U", Now);
        PublishedBudgetSnapshot second = PublishedBudgetSnapshot.Capture(government, year, version, funds, "u", "U", Now.AddMonths(6));

        first.MarkSuperseded(second, "u", Now.AddMonths(6));

        Assert.Equal(SnapshotStatus.Superseded, first.Status);
        Assert.True(second.IsActive);
        Assert.Throws<DomainException>(() => first.MarkSuperseded(second, "u", Now)); // already superseded
        Assert.Throws<DomainException>(() => second.MarkSuperseded(second, "u", Now)); // itself
    }

    [Fact]
    public void Snapshot_is_independent_of_later_changes_to_the_chart_of_accounts()
    {
        (Government government, FiscalYear year, BudgetVersion version, List<Fund> funds) = AdoptedBudget();
        PublishedBudgetSnapshot snapshot = PublishedBudgetSnapshot.Capture(government, year, version, funds, "u", "U", Now);

        funds[0].Update("1000", "General Operating Fund", FundCategory.General, null);

        Assert.Equal("General", snapshot.Funds.Single(f => f.Code == "1000").Name);
        Assert.Equal("General", snapshot.Lines.First().FundName);
    }
}
