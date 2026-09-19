using System.Text.RegularExpressions;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Governments;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

public sealed record GovernmentSettingsDto(
    Guid Id,
    string Name,
    GovernmentType Type,
    string State,
    int FiscalYearStartMonth,
    string PublicSlug,
    AppropriationLimitMode AppropriationLimitMode,
    string? Description,
    AccountNumberFormat AccountNumberFormat);

public sealed record UpdateGovernmentSettingsRequest(
    string Name,
    string PublicSlug,
    AppropriationLimitMode AppropriationLimitMode,
    string? Description,
    /// <summary>How account numbers are written: 4-3-4 with "-" for a UAN village, 3-3-4 for many county charts.</summary>
    int FundWidth,
    int DepartmentWidth,
    int ObjectWidth,
    string Separator,
    string DepartmentLabel);

public sealed partial class UpdateGovernmentSettingsRequestValidator : AbstractValidator<UpdateGovernmentSettingsRequest>
{
    public UpdateGovernmentSettingsRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Government.NameMaxLength);
        RuleFor(r => r.PublicSlug).NotEmpty().MaximumLength(Government.SlugMaxLength)
            .Matches(SlugPattern()).WithMessage("Use lowercase letters, digits, and single hyphens, e.g. maple-ridge-oh.");
        RuleFor(r => r.FundWidth).InclusiveBetween(AccountNumberFormat.MinWidth, AccountNumberFormat.MaxWidth);
        RuleFor(r => r.DepartmentWidth).InclusiveBetween(AccountNumberFormat.MinWidth, AccountNumberFormat.MaxWidth);
        RuleFor(r => r.ObjectWidth).InclusiveBetween(AccountNumberFormat.MinWidth, AccountNumberFormat.MaxWidth);
        RuleFor(r => r.Separator).NotEmpty().Length(1).WithMessage("One character, usually - or .");
        RuleFor(r => r.DepartmentLabel).NotEmpty().MaximumLength(20);
        RuleFor(r => r.AppropriationLimitMode).IsInEnum();
        RuleFor(r => r.Description).MaximumLength(4000);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public interface IGovernmentSettingsService
{
    Task<GovernmentSettingsDto> GetAsync(CancellationToken ct = default);
    Task<Result> UpdateAsync(UpdateGovernmentSettingsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Settings for the current tenant. Type, state, and fiscal year start month are not editable here:
/// changing the start month after fiscal years exist would make their stored dates wrong.
/// </summary>
public sealed class GovernmentSettingsService(
    ICivicBudgetDbContextFactory dbFactory,
    ITenantContext tenant,
    IValidator<UpdateGovernmentSettingsRequest> validator) : IGovernmentSettingsService
{
    public async Task<GovernmentSettingsDto> GetAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await LoadAsync(db, ct);
        return new GovernmentSettingsDto(
            government.Id, government.Name, government.Type, government.State, government.FiscalYearStartMonth,
            government.PublicSlug, government.AppropriationLimitMode, government.Description, government.AccountNumberFormat);
    }

    public async Task<Result> UpdateAsync(UpdateGovernmentSettingsRequest request, CancellationToken ct = default)
    {
        if (await validator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return invalid;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await LoadAsync(db, ct);

        // Slugs are global (they are public URLs), so this check is not tenant-scoped.
        if (await db.Governments.AnyAsync(g => g.PublicSlug == request.PublicSlug && g.Id != government.Id, ct))
        {
            return Result.Failure(nameof(request.PublicSlug), $"The address {request.PublicSlug} is used by another government.");
        }

        government.Rename(request.Name);
        government.SetPublicSlug(request.PublicSlug);
        government.SetAppropriationLimitMode(request.AppropriationLimitMode);
        government.SetDescription(request.Description);
        try
        {
            government.SetAccountNumberFormat(new AccountNumberFormat(request.FundWidth, request.DepartmentWidth, request.ObjectWidth, request.Separator, request.DepartmentLabel));
        }
        catch (DomainException ex)
        {
            return Result.Failure(nameof(request.Separator), ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Government> LoadAsync(ICivicBudgetDbContext db, CancellationToken ct)
    {
        Guid governmentId = tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set.");
        return await db.Governments.SingleAsync(g => g.Id == governmentId, ct);
    }
}
