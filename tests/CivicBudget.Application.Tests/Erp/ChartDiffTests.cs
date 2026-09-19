using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Tests.Erp;

/// <summary>What a sync would do, rule by rule: add, update, deactivate (never delete), reactivate, leave alone.</summary>
public class ChartDiffTests
{
    private static readonly LocalFund General = new(Guid.NewGuid(), "1000", "General Fund", FundCategory.General, "Day to day", true);
    private static readonly LocalFund Closed = new(Guid.NewGuid(), "2999", "Old Levy", FundCategory.SpecialRevenue, null, false);
    private static readonly LocalDepartment Police = new(Guid.NewGuid(), "110", "Police", null, true);
    private static readonly LocalObject Salaries = new(Guid.NewGuid(), "5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices, true);

    private static ErpChart Erp(IEnumerable<ErpFund>? funds = null, IEnumerable<ErpDepartment>? departments = null, IEnumerable<ErpObject>? objects = null) =>
        new((funds ?? []).ToList(), (departments ?? []).ToList(), (objects ?? []).ToList(), null);

    [Fact]
    public void Identical_charts_change_nothing()
    {
        ErpChart erp = Erp(
            [new("1000", "General Fund", FundCategory.General, "Day to day", true)],
            [new("110", "Police", null, true)],
            [new("5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices, true)]);

        IReadOnlyList<ChartChange> changes = ChartDiff.Compute(erp, [General], [Police], [Salaries]);

        Assert.All(changes, c => Assert.Equal(ChartChangeKind.Unchanged, c.Change));
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void New_codes_are_added_and_renamed_codes_are_updated_with_before_and_after()
    {
        ErpChart erp = Erp(
            [new("1000", "General Fund", FundCategory.General, "Day to day", true), new("2011", "Street Fund", FundCategory.SpecialRevenue, null, true)],
            [new("110", "Police Department", null, true)],
            [new("5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices, true)]);

        IReadOnlyList<ChartChange> changes = ChartDiff.Compute(erp, [General], [Police], [Salaries]);

        ChartChange street = changes.Single(c => c.Code == "2011");
        Assert.Equal(ChartChangeKind.Add, street.Change);
        Assert.Null(street.Before);
        ChartChange police = changes.Single(c => c.Code == "110");
        Assert.Equal(ChartChangeKind.Update, police.Change);
        Assert.Equal("Police", police.Before);
        Assert.Equal("Police Department", police.After);
    }

    [Fact]
    public void Codes_the_erp_no_longer_lists_or_marks_inactive_are_deactivated_not_deleted()
    {
        ErpChart erp = Erp(
            [new("1000", "General Fund", FundCategory.General, "Day to day", false)],
            [],
            [new("5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices, true)]);

        IReadOnlyList<ChartChange> changes = ChartDiff.Compute(erp, [General], [Police], [Salaries]);

        Assert.Equal(ChartChangeKind.Deactivate, changes.Single(c => c.Code == "1000").Change); // inactive in the ERP
        ChartChange police = changes.Single(c => c.Code == "110");
        Assert.Equal(ChartChangeKind.Deactivate, police.Change);                             // missing from the ERP
        Assert.Equal("(not in the ERP)", police.After);
        Assert.DoesNotContain(changes, c => c.Change == default);
    }

    [Fact]
    public void An_inactive_code_that_returns_is_reactivated_and_an_inactive_one_that_stays_away_is_left_alone()
    {
        ErpChart erp = Erp([new("2999", "Old Levy", FundCategory.SpecialRevenue, null, true)]);

        IReadOnlyList<ChartChange> changes = ChartDiff.Compute(erp, [Closed], [], []);
        IReadOnlyList<ChartChange> still = ChartDiff.Compute(Erp(), [Closed], [], []);

        Assert.Equal(ChartChangeKind.Reactivate, changes.Single().Change);
        Assert.Empty(still); // already inactive here and absent there: nothing to say
    }

    [Fact]
    public void Codes_match_regardless_of_case()
    {
        var fire = new LocalDepartment(Guid.NewGuid(), "FD", "Fire", null, true);
        ErpChart erp = Erp(departments: [new("fd", "Fire", null, true)]);

        Assert.Equal(ChartChangeKind.Unchanged, ChartDiff.Compute(erp, [], [fire], []).Single().Change);
    }

    [Fact]
    public void Preview_counts_follow_the_changes()
    {
        var preview = new ChartSyncPreviewDto("file", "chart.csv",
        [
            new("Fund", "1000", ChartChangeKind.Unchanged, "a", "a"),
            new("Fund", "2011", ChartChangeKind.Add, null, "b"),
            new("Object", "5110", ChartChangeKind.Update, "c", "d"),
        ], null);

        Assert.Equal(1, preview.Count(ChartChangeKind.Add));
        Assert.Equal(1, preview.Count(ChartChangeKind.Update));
        Assert.True(preview.HasChanges);
        Assert.False(new ChartSyncPreviewDto("file", "chart.csv", [new("Fund", "1000", ChartChangeKind.Unchanged, "a", "a")], null).HasChanges);
    }
}
