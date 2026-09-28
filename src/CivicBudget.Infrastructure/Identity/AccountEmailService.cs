using System.Buffers.Text;
using System.Text;
using CivicBudget.Application.Common;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Security;
using CivicBudget.Application.Users;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Notifications;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Reset and welcome links, built on Identity's password reset token: single use (it is tied to the
/// security stamp, which changes when the password does) and short lived
/// (<see cref="DataProtectionTokenProviderOptions.TokenLifespan"/>). The link carries the user id and
/// the token, never a password. A reset request is anonymous, so the service acts for the account's
/// own government to write the email and the audit row, and says nothing about whether the address
/// has an account, so the form cannot be used to find out who works where.
/// </summary>
public sealed class AccountEmailService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    CurrentUserContext context,
    IEmailOutbox outbox,
    IAppLinks links,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions,
    ISecurityEventLog securityLog,
    TimeProvider clock,
    ILogger<AccountEmailService> logger) : IAccountEmailService
{
    private const string ExpiredLink = "This link has expired or has already been used. Ask for a new one from the sign-in page.";

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        ApplicationUser? user = string.IsNullOrWhiteSpace(email) ? null : await userManager.FindByEmailAsync(email.Trim());
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            // Same response as a real account: logged here, invisible to whoever typed the address.
            logger.LogInformation("Password reset asked for an address with no usable account.");
            return;
        }

        context.SetTenant(user.GovernmentId);
        _ = await WriteLinkAsync(user, EmailKind.PasswordReset, (government, link) => EmailTemplates.PasswordReset(government, link, tokenOptions.Value.TokenLifespan),
            $"Password reset link sent to {user.Email}", "system", "system", ct);
    }

    public async Task<Result> ResetPasswordAsync(string userId, string code, string newPassword, CancellationToken ct = default)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        string? token = Decode(code);
        if (user is null || token is null)
        {
            return Result.Failure(ExpiredLink);
        }

        IdentityResult reset = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded)
        {
            return reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? Result.Failure(ExpiredLink)
                : Result.Failure(reset.Errors.Select(e => new ValidationError(nameof(newPassword), e.Description)));
        }

        // The person chose this password themselves, so it is no longer temporary, and following the
        // emailed link proves the address works.
        user.MustChangePassword = false;
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
        await userManager.ResetAccessFailedCountAsync(user);

        await securityLog.RecordAsync(Domain.Security.SecurityEventKind.PasswordChanged, user.GovernmentId, user.Id, user.Email, "From an emailed link", ct);
        context.SetTenant(user.GovernmentId);
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditEntries.Add(AuditEntry.Event(user.GovernmentId, "User", UserKey(user), $"{user.Email} set a new password from an emailed link",
            user.Id, user.DisplayName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SendWelcomeAsync(string userId, CancellationToken ct = default)
    {
        if (!context.IsInRole(Roles.Admin) || context.GovernmentId is not { } governmentId)
        {
            return Result.Failure("Only an Administrator can send a sign-in link.");
        }

        ApplicationUser? user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId && u.GovernmentId == governmentId, ct);
        if (user?.Email is null)
        {
            return Result.Failure("User was not found.");
        }

        string role = Roles.DisplayName((await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "");
        await WriteWelcomeAsync(user, role, context.DisplayName ?? "Your administrator", context.UserId ?? "system", ct);
        return Result.Success();
    }

    /// <summary>
    /// The welcome email with its sign-in link, for a user who may belong to a government nobody is
    /// signed in to yet (a new government's first administrator). Returns the link.
    /// </summary>
    public async Task<string> WriteWelcomeAsync(ApplicationUser user, string role, string addedBy, string actorId, CancellationToken ct = default)
    {
        context.SetTenant(user.GovernmentId);
        return await WriteLinkAsync(user, EmailKind.Welcome, (government, link) => EmailTemplates.Welcome(government, role, addedBy, link, tokenOptions.Value.TokenLifespan),
            $"Sign-in link sent to {user.Email}", actorId, addedBy, ct);
    }

    /// <summary>Writes the email and its audit row in one save, then wakes the sender.</summary>
    private async Task<string> WriteLinkAsync(ApplicationUser user, EmailKind kind, Func<string, string, EmailContent> write, string auditDescription,
        string actorId, string actorName, CancellationToken ct)
    {
        string token = await userManager.GeneratePasswordResetTokenAsync(user);
        string link = links.Absolute($"Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&code={Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token))}");

        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        string government = await db.Governments.Where(g => g.Id == user.GovernmentId).Select(g => g.Name).SingleAsync(ct);
        outbox.Add(db, user.GovernmentId, kind, new EmailRecipient(user.Id, user.Email!, user.DisplayName), write(government, link));
        db.AuditEntries.Add(AuditEntry.Event(user.GovernmentId, "User", UserKey(user), auditDescription, actorId, actorName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        outbox.Notify();
        return link;
    }

    private static string? Decode(string code)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Guid UserKey(ApplicationUser user) => Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty;
}
