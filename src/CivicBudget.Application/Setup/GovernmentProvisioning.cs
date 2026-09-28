using System.Text.RegularExpressions;
using CivicBudget.Application.Common;
using CivicBudget.Domain.Governments;
using FluentValidation;

namespace CivicBudget.Application.Setup;

/// <param name="PublicSlug">The public portal's address segment, e.g. "cedar-falls-oh".</param>
public sealed record ProvisionGovernmentRequest(
    string Name,
    GovernmentType Type,
    string State,
    int FiscalYearStartMonth,
    string PublicSlug,
    string AdminName,
    string AdminEmail);

/// <param name="SignInLink">The first administrator's link to choose a password; also in the outbox, and emailed where a mail server is set up.</param>
public sealed record ProvisionedGovernmentDto(Guid GovernmentId, string AdminUserId, string SignInLink);

public sealed partial class ProvisionGovernmentRequestValidator : AbstractValidator<ProvisionGovernmentRequest>
{
    public ProvisionGovernmentRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Government.NameMaxLength);
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.State).NotEmpty().Length(2).Matches("^[A-Za-z]{2}$").WithMessage("A two-letter state code, e.g. OH.");
        RuleFor(r => r.FiscalYearStartMonth).InclusiveBetween(1, 12);
        RuleFor(r => r.PublicSlug).NotEmpty().MaximumLength(Government.SlugMaxLength)
            .Matches(SlugPattern()).WithMessage("Use lowercase letters, digits, and single hyphens, e.g. cedar-falls-oh.");
        RuleFor(r => r.AdminName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

/// <summary>
/// Sets up a new government: the government itself and its first Administrator, who is emailed a
/// link to choose a password and then finishes the setup in the app (the getting-started checklist).
/// This is the vendor's operation, not a tenant's, so it is not on any page a government's own users
/// can reach; it runs from the command line (<c>--provision</c>) with the deployment's own credentials.
/// </summary>
public interface IGovernmentProvisioningService
{
    Task<Result<ProvisionedGovernmentDto>> ProvisionAsync(ProvisionGovernmentRequest request, string provisionedBy, CancellationToken ct = default);
}
