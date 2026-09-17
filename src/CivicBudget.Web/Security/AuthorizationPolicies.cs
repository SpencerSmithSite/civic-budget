using CivicBudget.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace CivicBudget.Web.Security;

/// <summary>
/// The single place that maps policy names to roles. Pages say <c>[Authorize(Policy = Policies.CanPublish)]</c>;
/// this class says "CanPublish means Finance Director". AuthorizationPolicyTests exercise every row.
/// </summary>
public static class AuthorizationPolicies
{
    public static AuthorizationBuilder AddCivicBudgetPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(Policies.CanManageUsers, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.CanMaintainSetup, p => p.RequireRole(Roles.Admin, Roles.FinanceDirector))
            .AddPolicy(Policies.CanViewBudget, p => p.RequireRole(Roles.Admin, Roles.FinanceDirector, Roles.DepartmentHead, Roles.Viewer))
            .AddPolicy(Policies.CanEditBeginningBalances, p => p.RequireRole(Roles.FinanceDirector))
            .AddPolicy(Policies.CanAdvanceWorkflow, p => p.RequireRole(Roles.FinanceDirector))
            .AddPolicy(Policies.CanPublish, p => p.RequireRole(Roles.FinanceDirector))
            .AddPolicy(Policies.CanImport, p => p.RequireRole(Roles.FinanceDirector))
            .AddPolicy(Policies.CanViewAudit, p => p.RequireRole(Roles.Admin, Roles.FinanceDirector, Roles.DepartmentHead))
            // Resource-based: the handler needs the line and version, so the policy only names the requirement.
            .AddPolicy(Policies.CanEditBudgetLine, p => p.AddRequirements(new BudgetLineEditRequirement()));
}
