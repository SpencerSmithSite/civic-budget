using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Accounts;

/// <summary>
/// An entry in the chart of accounts. Codes are tenant-defined strings and are *not* parsed into
/// fund/department segments; a budget line carries fund, department, and account separately.
/// </summary>
[Audited]
public sealed class Account : Entity, ITenantOwned
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    public Guid GovernmentId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public ReportingCategory Category { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Account(Guid governmentId, string code, string name, AccountType type, ReportingCategory category)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        GovernmentId = governmentId;
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        (Type, Category) = ValidateTypeAndCategory(type, category);
    }

    private Account()
    {
        Code = null!;
        Name = null!;
    }

    public void Update(string code, string name, AccountType type, ReportingCategory category)
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        (Type, Category) = ValidateTypeAndCategory(type, category);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private static (AccountType, ReportingCategory) ValidateTypeAndCategory(AccountType type, ReportingCategory category)
    {
        Guard.Against(
            !ReportingCategoryRules.IsValidFor(category, type),
            $"Reporting category {category} is not valid for account type {type}.");
        return (type, category);
    }
}
