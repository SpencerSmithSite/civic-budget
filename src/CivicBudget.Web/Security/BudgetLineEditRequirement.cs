using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace CivicBudget.Web.Security;

/// <summary>Marker requirement for the CanEditBudgetLine policy. The logic is in the handler.</summary>
public sealed class BudgetLineEditRequirement : IAuthorizationRequirement;

/// <summary>What the handler needs to know about the line being edited. A DTO, so callers do not need a tracked entity.</summary>
public sealed record BudgetLineResource(BudgetStatus VersionStatus, Guid? DepartmentId);

/// <summary>
/// Resource-based authorization: <c>authorizationService.AuthorizeAsync(user, resource, Policies.CanEditBudgetLine)</c>.
/// Role checks alone cannot answer "may this department user edit *this* line", because the answer
/// depends on the line's department and the version's status. The rule itself lives in
/// <see cref="BudgetLinePermissions"/> so services and tests share it; this class only adapts the
/// ASP.NET Core principal to that rule.
/// </summary>
public sealed class BudgetLineEditHandler : AuthorizationHandler<BudgetLineEditRequirement, BudgetLineResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BudgetLineEditRequirement requirement,
        BudgetLineResource resource)
    {
        var user = new ClaimsPrincipalUser(context.User);
        if (BudgetLinePermissions.CanEdit(user, resource.VersionStatus, resource.DepartmentId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
