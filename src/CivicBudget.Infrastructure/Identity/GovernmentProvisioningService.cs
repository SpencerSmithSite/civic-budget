using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

public sealed class GovernmentProvisioningService(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    CurrentUserContext context,
    AccountEmailService accountEmail,
    IValidator<ProvisionGovernmentRequest> validator,
    TimeProvider clock) : IGovernmentProvisioningService
{
    public async Task<Result<ProvisionedGovernmentDto>> ProvisionAsync(ProvisionGovernmentRequest request, string provisionedBy, CancellationToken ct = default)
    {
        if (await validator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<ProvisionedGovernmentDto>(invalid.Errors);
        }

        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Governments.AnyAsync(g => g.PublicSlug == request.PublicSlug, ct))
        {
            return Result.Failure<ProvisionedGovernmentDto>(nameof(request.PublicSlug), $"The address {request.PublicSlug} is used by another government.");
        }

        if (await userManager.FindByEmailAsync(request.AdminEmail) is not null)
        {
            return Result.Failure<ProvisionedGovernmentDto>(nameof(request.AdminEmail), "A user with that email already exists.");
        }

        var government = new Government(request.Name.Trim(), request.Type, request.State, request.FiscalYearStartMonth, request.PublicSlug);
        context.SetTenant(government.Id); // before the save: the government's own audit row is tenant-checked
        db.Governments.Add(government);
        db.AuditEntries.Add(AuditEntry.Event(government.Id, nameof(Government), government.Id, $"Set up {government.Name} in CivicBudget",
            "system", provisionedBy, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        // No password: the administrator chooses one from the emailed link, so nobody else ever knows it.
        var admin = new ApplicationUser
        {
            UserName = request.AdminEmail.Trim(),
            Email = request.AdminEmail.Trim(),
            DisplayName = request.AdminName.Trim(),
            GovernmentId = government.Id,
        };
        IdentityResult created = await userManager.CreateAsync(admin);
        IdentityResult role = created.Succeeded ? await userManager.AddToRoleAsync(admin, Roles.Admin) : created;
        if (!role.Succeeded)
        {
            return Result.Failure<ProvisionedGovernmentDto>(role.Errors.Select(e => new ValidationError(nameof(request.AdminEmail), e.Description)));
        }

        string link = await accountEmail.WriteWelcomeAsync(admin, Roles.DisplayName(Roles.Admin), provisionedBy, "system", ct);
        return Result.Success(new ProvisionedGovernmentDto(government.Id, admin.Id, link));
    }
}
