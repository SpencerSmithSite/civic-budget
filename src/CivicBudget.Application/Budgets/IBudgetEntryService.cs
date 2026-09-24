using CivicBudget.Application.Common;
using CivicBudget.Domain.Common;
using FluentValidation;

namespace CivicBudget.Application.Budgets;

/// <summary>
/// Budget entry for one version: what the current user may see and change, and the fund balances
/// that result. Every mutation re-checks <see cref="Security.BudgetLinePermissions"/> against the
/// version's current status, so a stale screen cannot edit a version that was adopted a minute ago.
/// </summary>
public interface IBudgetEntryService
{
    Task<IReadOnlyList<BudgetVersionSummaryDto>> ListVersionsAsync(CancellationToken ct = default);
    Task<BudgetWorkspaceDto?> GetWorkspaceAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> UpdateLineAmountAsync(Guid versionId, Guid lineId, decimal amount, CancellationToken ct = default);
    Task<Result> UpdateLineJustificationAsync(Guid versionId, Guid lineId, string? justification, CancellationToken ct = default);
    Task<Result<Guid>> AddLineAsync(AddBudgetLineRequest request, CancellationToken ct = default);
    Task<Result> RemoveLineAsync(Guid versionId, Guid lineId, CancellationToken ct = default);
    Task<Result> SetBeginningBalanceAsync(Guid versionId, Guid fundId, decimal amount, CancellationToken ct = default);
}

public sealed class AddBudgetLineRequestValidator : AbstractValidator<AddBudgetLineRequest>
{
    public AddBudgetLineRequestValidator()
    {
        RuleFor(r => r.BudgetVersionId).NotEmpty();
        RuleFor(r => r.FundId).NotEmpty();
        RuleFor(r => r.AccountId).NotEmpty();
        RuleFor(r => r.Amount).GreaterThanOrEqualTo(0m).WithMessage("Budgeted amounts cannot be negative.")
            .LessThanOrEqualTo(Money.MaxAmount).WithMessage(Money.TooLargeMessage);
        RuleFor(r => r.PriorYearActual).GreaterThanOrEqualTo(0m).WithMessage("Prior year actuals cannot be negative.")
            .LessThanOrEqualTo(Money.MaxAmount).WithMessage(Money.TooLargeMessage);
        RuleFor(r => r.CurrentYearBudget).GreaterThanOrEqualTo(0m).WithMessage("Current year budgets cannot be negative.")
            .LessThanOrEqualTo(Money.MaxAmount).WithMessage(Money.TooLargeMessage);
        RuleFor(r => r.Justification).MaximumLength(Domain.Budgets.BudgetLine.JustificationMaxLength);
    }
}
