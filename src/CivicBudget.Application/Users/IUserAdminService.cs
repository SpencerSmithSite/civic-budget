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
    bool IsLockedOut);

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string Role, IReadOnlyList<Guid> DepartmentIds);

public sealed record UpdateUserRequest(string Id, string DisplayName, string Role, IReadOnlyList<Guid> DepartmentIds);

public sealed record ResetPasswordRequest(string Id, string NewPassword);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Password).NotEmpty().MinimumLength(10);
        RuleFor(r => r.Role).Must(Roles.All.Contains).WithMessage("Choose one of the four roles.");
        RuleFor(r => r.DepartmentIds).NotEmpty()
            .When(r => r.Role == Roles.DepartmentHead)
            .WithMessage("A Department Head must be assigned at least one department.");
        RuleFor(r => r.DepartmentIds).Empty()
            .When(r => r.Role != Roles.DepartmentHead)
            .WithMessage("Only Department Heads are assigned departments.");
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
            .WithMessage("A Department Head must be assigned at least one department.");
        RuleFor(r => r.DepartmentIds).Empty()
            .When(r => r.Role != Roles.DepartmentHead)
            .WithMessage("Only Department Heads are assigned departments.");
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
