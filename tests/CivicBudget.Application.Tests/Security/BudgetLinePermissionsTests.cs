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
    [InlineData(Roles.Viewer)]
    [InlineData(null)]
    public void Viewers_and_anonymous_never_edit_lines(string? role)
    {
        var user = new FakeUser(role, Police);
        Assert.False(BudgetLinePermissions.CanEdit(user, BudgetStatus.Draft, Police));
    }

    [Fact]
    public void An_administrator_edits_like_the_fiscal_officer()
    {
        var admin = new FakeUser(Roles.Admin);
        Assert.True(BudgetLinePermissions.CanEdit(admin, BudgetStatus.Draft, Police));
        Assert.True(BudgetLinePermissions.CanEdit(admin, BudgetStatus.Proposed, Streets));
        Assert.False(BudgetLinePermissions.CanEdit(admin, BudgetStatus.Adopted, Streets));
        Assert.True(BudgetLinePermissions.CanEditBeginningBalances(admin, BudgetStatus.Draft));
    }

    [Fact]
    public void Fiscal_authority_edits_beginning_balances_and_not_after_adoption()
    {
        Assert.True(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Draft));
        Assert.True(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Proposed));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.FinanceDirector), BudgetStatus.Adopted));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.DepartmentHead, Police), BudgetStatus.Draft));
        Assert.False(BudgetLinePermissions.CanEditBeginningBalances(new FakeUser(Roles.Viewer), BudgetStatus.Draft));
    }

    // ---- Phase 9d: department requests -------------------------------------------------------------

    [Fact]
    public void Submitting_locks_the_department_for_its_users_but_not_for_the_fiscal_authority()
    {
        var chief = new FakeUser(Roles.DepartmentHead, Police);
        var officer = new FakeUser(Roles.FinanceDirector);

        Assert.False(BudgetLinePermissions.CanEdit(chief, BudgetStatus.Draft, Police, departmentSubmitted: true));
        Assert.False(BudgetLinePermissions.CanAddLine(chief, BudgetStatus.Draft, Police, departmentSubmitted: true));
        Assert.False(BudgetLinePermissions.CanEditNarrative(chief, BudgetStatus.Draft, Police, departmentSubmitted: true));
        Assert.True(BudgetLinePermissions.CanEditNarrative(chief, BudgetStatus.Draft, Police, departmentSubmitted: false));

        Assert.True(BudgetLinePermissions.CanEdit(officer, BudgetStatus.Draft, Police, departmentSubmitted: true));
        Assert.True(BudgetLinePermissions.CanEditNarrative(officer, BudgetStatus.Proposed, Police, departmentSubmitted: true));
    }

    [Theory]
    [InlineData(DepartmentRequestStatus.InProgress, true)]
    [InlineData(DepartmentRequestStatus.Returned, true)]
    [InlineData(DepartmentRequestStatus.Submitted, false)]
    public void A_department_user_submits_own_department_while_draft_and_not_twice(DepartmentRequestStatus status, bool expected)
    {
        var chief = new FakeUser(Roles.DepartmentHead, Police);

        Assert.Equal(expected, BudgetLinePermissions.CanSubmitDepartment(chief, BudgetStatus.Draft, Police, status));
        Assert.False(BudgetLinePermissions.CanSubmitDepartment(chief, BudgetStatus.Draft, Streets, status));
        Assert.False(BudgetLinePermissions.CanSubmitDepartment(chief, BudgetStatus.Proposed, Police, status));
        Assert.False(BudgetLinePermissions.CanSubmitDepartment(new FakeUser(Roles.Viewer), BudgetStatus.Draft, Police, status));
    }

    [Fact]
    public void Only_the_fiscal_authority_returns_and_only_a_submitted_request()
    {
        Assert.True(BudgetLinePermissions.CanReturnDepartment(new FakeUser(Roles.FinanceDirector), BudgetStatus.Draft, DepartmentRequestStatus.Submitted));
        Assert.True(BudgetLinePermissions.CanReturnDepartment(new FakeUser(Roles.Admin), BudgetStatus.Draft, DepartmentRequestStatus.Submitted));
        Assert.True(BudgetLinePermissions.CanSubmitDepartment(new FakeUser(Roles.Admin), BudgetStatus.Draft, Streets, DepartmentRequestStatus.InProgress)); // on a department's behalf
        Assert.False(BudgetLinePermissions.CanReturnDepartment(new FakeUser(Roles.FinanceDirector), BudgetStatus.Draft, DepartmentRequestStatus.InProgress));
        Assert.False(BudgetLinePermissions.CanReturnDepartment(new FakeUser(Roles.FinanceDirector), BudgetStatus.Proposed, DepartmentRequestStatus.Submitted));
        Assert.False(BudgetLinePermissions.CanReturnDepartment(new FakeUser(Roles.DepartmentHead, Police), BudgetStatus.Draft, DepartmentRequestStatus.Submitted));
    }
}
