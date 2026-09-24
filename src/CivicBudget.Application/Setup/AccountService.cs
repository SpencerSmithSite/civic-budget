using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Accounts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

public sealed record AccountDto(Guid Id, string Code, string Name, AccountType Type, ReportingCategory Category, bool IsActive);

public sealed record SaveAccountRequest(Guid? Id, string Code, string Name, AccountType Type, ReportingCategory Category);

public sealed class SaveAccountRequestValidator : AbstractValidator<SaveAccountRequest>
{
    public SaveAccountRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(Account.CodeMaxLength);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Account.NameMaxLength);
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.Category).IsInEnum();

        // The domain enforces this too (Account's constructor throws); checking here turns it into a
        // field-level message instead of a crash.
        RuleFor(r => r.Category)
            .Must((request, category) => ReportingCategoryRules.IsValidFor(category, request.Type))
            .WithMessage(r => $"{r.Category} is not a valid category for a {r.Type} account.");
    }
}

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<AccountDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> SaveAsync(SaveAccountRequest request, CancellationToken ct = default);
    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
}

/// <summary>Chart of accounts maintenance. Same shape as <see cref="FundService"/>; see its remarks.</summary>
public sealed class AccountService(
    ICivicBudgetDbContextFactory dbFactory,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IValidator<SaveAccountRequest> validator) : IAccountService
{
    public async Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Accounts
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Code)
            .Select(a => ToDto(a))
            .ToListAsync(ct);
    }

    public async Task<AccountDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        return account is null ? null : ToDto(account);
    }

    public async Task<Result<Guid>> SaveAsync(SaveAccountRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<Guid>(SetupNotAllowed.FiscalAuthority);
        }

        if (await validator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<Guid>(invalid.Errors);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await ChartOwnership.RefuseIfErpManagedAsync(db, tenant.GovernmentId, ct) is { } managed)
        {
            return Result.Failure<Guid>(managed.Errors);
        }

        string code = request.Code.Trim();
        if (await db.Accounts.AnyAsync(a => a.Code == code && a.Id != request.Id, ct))
        {
            return Result.Failure<Guid>(nameof(request.Code), $"Account code {code} is already in use.");
        }

        Account account;
        if (request.Id is { } id)
        {
            account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new KeyNotFoundException($"Account {id} was not found.");

            // Changing the type of an account that already has budget lines would silently move money
            // between resources and appropriations in every open version. Refuse it.
            if (account.Type != request.Type && await db.BudgetLines.AnyAsync(l => l.AccountId == id, ct))
            {
                return Result.Failure<Guid>(nameof(request.Type), "The account type cannot change once the account is used on budget lines. Retire this account and create a new one.");
            }

            account.Update(code, request.Name, request.Type, request.Category);
        }
        else
        {
            Guid governmentId = tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set; cannot create an account.");
            account = new Account(governmentId, code, request.Name, request.Type, request.Category);
            db.Accounts.Add(account);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(account.Id);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(SetupNotAllowed.FiscalAuthority);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await ChartOwnership.RefuseIfErpManagedAsync(db, tenant.GovernmentId, ct) is { } managed)
        {
            return managed;
        }

        Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return Result.Failure("Account was not found.");
        }

        if (isActive)
        {
            account.Reactivate();
        }
        else
        {
            account.Deactivate();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static AccountDto ToDto(Account a) => new(a.Id, a.Code, a.Name, a.Type, a.Category, a.IsActive);
}
