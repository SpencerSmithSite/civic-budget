namespace CivicBudget.Domain.Budgets;

/// <summary>Workflow: Draft → Proposed → Adopted. Adopted is terminal; changes require an amendment.</summary>
public enum BudgetStatus
{
    Draft = 1,
    Proposed = 2,
    Adopted = 3,
}
