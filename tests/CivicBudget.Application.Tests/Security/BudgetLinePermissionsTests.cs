using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;

namespace CivicBudget.Application.Tests.Security;

/// <summary>SPEC section 7.1: who may edit which line, and when.</summary>
public class BudgetLinePermissionsTests
{
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid Streets = Guid.CreateVersion7();

    [Theory]
    [InlineData(BudgetStatus.Draft, true)]
    [InlineData(BudgetStatus.Proposed, true)]
    [InlineData(BudgetStatus.Adopted, false)]
    public void Finance_director_edits_any_line_until_adoption(BudgetStatus status, bool expected)
    {
        var fd = new FakeUser(Roles.FinanceDirector);
        Assert.Equal(expected, BudgetLinePermissions.CanEdit(fd, status, Police));
        Assert.Equal(expected, BudgetLinePermissions.CanEdit(fd, status, lineDepartmentId: null));
    }

    [Fact]
    public void Department_head_edits_only_own_departments_while_draft()
    {
        var chief = new FakeUser(Roles.DepartmentHead, Police);

        Assert.True(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Draft, Police));
        Assert.False(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Draft, Streets));
        Assert.False(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Draft, lineDepartmentId: null)); // fund-level revenue line
        Assert.False(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Proposed, Police));
        Assert.False(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Adopted, Police));
    }

    [Fact]
    public void Department_head_with_several_departments_edits_each()
    {
        var director = new FakeUser(Roles.DepartmentHead, Police, Streets);
        Assert.True(BudgetLinePermissions.CanEdit(director, BudgetStatus.Draft, Police));
        Assert.True(BudgetLinePermissions.CanEdit(director, BudgetStatus.Draft, Streets));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Viewer)]
    [InlineData(null)]
    public void Admins_viewers_and_anonymous_never_edit_lines(string? role)
    {
        var user = new FakeUser(role, Police);
        Assert.False(BudgetLinePermissions.CanEdit(user, BudgetStatus.Draft, Police));
    }

    [Fact]
    public void Only_the_finance_director_edits_beginning_balances_and_not_after_adoption()
    {
        Assert.True(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Draft));
        Assert.True(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Proposed));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Adopted));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.DepartmentHead, Police), BudgetStatus.Draft));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.Admin), BudgetStatus.Draft));
    }
}
