using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Web.Tests.Security;

/// <summary>
/// Evaluates the real policies through the real <see cref="IAuthorizationService"/> with hand-built
/// principals. No browser, no database: this is the authorization matrix from SPEC section 7.1 as a test.
/// </summary>
public class AuthorizationPolicyTests
{
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid Streets = Guid.CreateVersion7();

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddCivicBudgetPolicies();
        services.AddScoped<IAuthorizationHandler, BudgetLineEditHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal User(string role, params Guid[] departments)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, Guid.Empty.ToString()));
        foreach (Guid department in departments)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, department.ToString()));
        }

        return new ClaimsPrincipal(identity);
    }

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public static TheoryData<string, string, bool> Matrix()
    {
        // (policy, role, allowed). Every cell of the SPEC table.
        var data = new TheoryData<string, string, bool>();
        void Row(string policy, params string[] allowedRoles)
        {
            foreach (string role in Roles.All)
            {
                data.Add(policy, role, allowedRoles.Contains(role));
            }
        }

        Row(Policies.CanManageUsers, Roles.Admin);
        Row(Policies.CanMaintainSetup, Roles.Admin, Roles.FinanceDirector);
        Row(Policies.CanViewBudget, Roles.Admin, Roles.FinanceDirector, Roles.DepartmentHead, Roles.Viewer);
        Row(Policies.CanEditBeginningBalances, Roles.FinanceDirector);
        Row(Policies.CanAdvanceWorkflow, Roles.FinanceDirector);
        Row(Policies.CanPublish, Roles.FinanceDirector);
        Row(Policies.CanImport, Roles.FinanceDirector);
        Row(Policies.CanViewAudit, Roles.Admin, Roles.FinanceDirector, Roles.DepartmentHead);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Role_policies_match_the_spec_matrix(string policy, string role, bool allowed)
    {
        IAuthorizationService authorization = BuildAuthorizationService();

        AuthorizationResult result = await authorization.AuthorizeAsync(User(role), resource: null, policy);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Theory]
    [InlineData(Policies.CanManageUsers)]
    [InlineData(Policies.CanViewBudget)]
    [InlineData(Policies.CanPublish)]
    public async Task Anonymous_users_pass_no_policy(string policy)
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        Assert.False((await authorization.AuthorizeAsync(Anonymous, resource: null, policy)).Succeeded);
    }

    [Fact]
    public async Task Line_edit_policy_is_resource_based()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal chief = User(Roles.DepartmentHead, Police);
        ClaimsPrincipal fd = User(Roles.FinanceDirector);

        Assert.True((await authorization.AuthorizeAsync(chief, new BudgetLineResource(BudgetStatus.Draft, Police), Policies.CanEditBudgetLine)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(chief, new BudgetLineResource(BudgetStatus.Draft, Streets), Policies.CanEditBudgetLine)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(chief, new BudgetLineResource(BudgetStatus.Proposed, Police), Policies.CanEditBudgetLine)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(fd, new BudgetLineResource(BudgetStatus.Proposed, Streets), Policies.CanEditBudgetLine)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(fd, new BudgetLineResource(BudgetStatus.Adopted, Streets), Policies.CanEditBudgetLine)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(User(Roles.Viewer), new BudgetLineResource(BudgetStatus.Draft, Police), Policies.CanEditBudgetLine)).Succeeded);
    }
}
