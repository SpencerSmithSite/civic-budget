using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using FluentValidation;

namespace CivicBudget.Application.Users;

public sealed record UserSummaryDto(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<string> DepartmentCodes,
    bool IsLockedOut,
    /// <summary>Ticks of the last picture upload, for the image URL; null when the user shows initials.</summary>
    long? AvatarVersion = null,
    /// <summary>Whether the user signs in with a second step (an authenticator app).</summary>
    bool TwoFactorEnabled = false);

/// <param name="Password">A temporary password the administrator will share, or null to email the user a link to choose their own.</param>
public sealed record CreateUserRequest(string Email, string DisplayName, string? Password, string Role, IReadOnlyList<Guid> DepartmentIds);

public sealed record UpdateUserRequest(string Id, string DisplayName, string Role, IReadOnlyList<Guid> DepartmentIds);

public sealed record ResetPasswordRequest(string Id, string NewPassword);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Password).MinimumLength(10).When(r => !string.IsNullOrEmpty(r.Password));
        RuleFor(r => r.Role).Must(Roles.All.Contains).WithMessage("Choose one of the four roles.");
        RuleFor(r => r.DepartmentIds).NotEmpty()
            .When(r => r.Role == Roles.DepartmentHead)
            .WithMessage("A department user must be assigned at least one department.");
        RuleFor(r => r.DepartmentIds).Empty()
            .When(r => r.Role != Roles.DepartmentHead)
            .WithMessage("Only department users are assigned departments.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Role).Must(Roles.All.Contains).WithMessage("Choose one of the four roles.");
        RuleFor(r => r.DepartmentIds).NotEmpty()
            .When(r => r.Role == Roles.DepartmentHead)
            .WithMessage("A department user must be assigned at least one department.");
        RuleFor(r => r.DepartmentIds).Empty()
            .When(r => r.Role != Roles.DepartmentHead)
            .WithMessage("Only department users are assigned departments.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(10);
    }
}

/// <summary>
/// User administration for the current government. Implemented in Infrastructure because it needs
/// ASP.NET Core Identity's UserManager; the Application layer only knows this contract.
/// Identity tables are outside the tenant query filter (sign-in must find a user before a tenant is
/// known), so the implementation scopes every query by the current government explicitly.
/// </summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<UserSummaryDto?> GetAsync(string id, CancellationToken ct = default);
    Task<Result<string>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<Result> UpdateAsync(UpdateUserRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result> SetLockedOutAsync(string id, bool isLockedOut, CancellationToken ct = default);
}

/// <summary>
/// The emails that let people into their accounts: a reset link for a forgotten password, and a
/// welcome link that lets a new user choose their first password. Nobody's password is ever sent
/// by email, and nothing here says whether an address has an account.
/// </summary>
public interface IAccountEmailService
{
    /// <summary>Writes a reset link to the account with this address, if there is one; says nothing either way.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);

    /// <summary>Sets a new password from an emailed link; an expired or used link is refused.</summary>
    Task<Result> ResetPasswordAsync(string userId, string code, string newPassword, CancellationToken ct = default);

    /// <summary>An administrator sends a user a link to choose their password (a new account, or one whose password they forgot).</summary>
    Task<Result> SendWelcomeAsync(string userId, CancellationToken ct = default);
}

/// <summary>A government's sign-in security: whether two-step sign-in is required, and who has it.</summary>
public sealed record SignInSecurityDto(bool RequireMfa, int UsersWithMfa, int UsersWithoutMfa);

/// <summary>
/// Two-step sign-in for a government, for its Administrators: require it of everyone, and clear it
/// for a user who lost their phone (they set it up again at their next sign-in). Each change is an
/// audit event.
/// </summary>
public interface ISignInSecurityService
{
    Task<SignInSecurityDto> GetAsync(CancellationToken ct = default);
    Task<Result> SetRequireMfaAsync(bool require, CancellationToken ct = default);
    Task<Result> ResetTwoFactorAsync(string userId, CancellationToken ct = default);
}
