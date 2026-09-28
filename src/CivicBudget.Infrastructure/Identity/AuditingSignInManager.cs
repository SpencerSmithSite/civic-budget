using CivicBudget.Application.Security;
using CivicBudget.Domain.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Identity's sign-in manager, recording every outcome in the security log: signed in, wrong
/// password, unknown address, locked out, second step asked for, second step failed, recovery code
/// used, signed out. Doing it here rather than on each page means a sign-in path added later is
/// logged without anyone remembering to.
/// </summary>
public sealed class AuditingSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    ISecurityEventLog securityLog)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>Set on the request by the idle sign-out, so the log says the session expired rather than that the person left.</summary>
    public const string IdleSignOutItem = "cb-idle-sign-out";

    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        ApplicationUser? user = await UserManager.FindByNameAsync(userName);
        if (user is null)
        {
            await securityLog.RecordAsync(SecurityEventKind.UnknownAccount, null, null, userName);
            return SignInResult.Failed;
        }

        return await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
    }

    public override async Task<SignInResult> PasswordSignInAsync(ApplicationUser user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        SignInResult result = await base.PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
        SecurityEventKind kind = result.Succeeded ? SecurityEventKind.SignedIn
            : result.RequiresTwoFactor ? SecurityEventKind.TwoStepRequired
            : result.IsLockedOut ? SecurityEventKind.LockedOut
            : SecurityEventKind.SignInFailed;
        await Record(kind, user, isPersistent && result.Succeeded ? "Remembered on this device" : null);
        return result;
    }

    public override async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient)
    {
        ApplicationUser? user = await GetTwoFactorAuthenticationUserAsync();
        SignInResult result = await base.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);
        if (user is not null)
        {
            await Record(result.Succeeded ? SecurityEventKind.SignedIn : result.IsLockedOut ? SecurityEventKind.LockedOut : SecurityEventKind.TwoStepFailed, user,
                result.Succeeded ? "With an authenticator code" + (rememberClient ? "; browser remembered for 14 days" : "") : null);
        }

        return result;
    }

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        ApplicationUser? user = await GetTwoFactorAuthenticationUserAsync();
        SignInResult result = await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (user is not null)
        {
            await Record(result.Succeeded ? SecurityEventKind.RecoveryCodeUsed : result.IsLockedOut ? SecurityEventKind.LockedOut : SecurityEventKind.TwoStepFailed, user,
                result.Succeeded ? $"{await UserManager.CountRecoveryCodesAsync(user)} recovery codes left" : null);
        }

        return result;
    }

    public override async Task SignOutAsync()
    {
        ApplicationUser? user = Context.User.Identity?.IsAuthenticated == true ? await UserManager.GetUserAsync(Context.User) : null;
        await base.SignOutAsync();
        if (user is not null)
        {
            await Record(Context.Items.ContainsKey(IdleSignOutItem) ? SecurityEventKind.SessionExpired : SecurityEventKind.SignedOut, user, null);
        }
    }

    private Task Record(SecurityEventKind kind, ApplicationUser user, string? detail) =>
        securityLog.RecordAsync(kind, user.GovernmentId, user.Id, user.Email, detail);
}
