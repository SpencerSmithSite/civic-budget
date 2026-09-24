namespace CivicBudget.Domain.Governments;

/// <summary>The kind of local government. Only used for display; the budget rules are the same for each.</summary>
public enum GovernmentType
{
    City = 1,
    Village = 2,
    Township = 3,
    County = 4,
}
