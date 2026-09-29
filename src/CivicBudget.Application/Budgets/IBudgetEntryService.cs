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

    /// <summary>
    /// Several amounts at once, all or none: every line must be one the user may edit and still hold
    /// the amount the change was worked out from, or nothing is saved. For changes proposed and then
    /// confirmed, where the budget may have moved in between.
    /// </summary>
    Task<Result> UpdateLineAmountsAsync(Guid versionId, IReadOnlyList<LineAmountChange> changes, CancellationToken ct = default);
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

/// <param name="Expected">The amount the change was worked out from; if the line holds anything else now, the change is refused.</param>
public sealed record LineAmountChange(Guid LineId, decimal Expected, decimal Amount);
