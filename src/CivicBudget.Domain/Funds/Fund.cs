using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Funds;

/// <summary>
/// A fund: a separate pot of money with its own balance and its own legal limits on what it can pay
/// for (General, Street Construction Maintenance &amp; Repair, Water). Accountants call it a
/// "self-balancing set of accounts". Codes are whatever the government uses; the seed data uses
/// Ohio UAN-style numbers.
/// </summary>
[Audited]
public sealed class Fund : Entity, ITenantOwned
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    public Guid GovernmentId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public FundCategory Category { get; private set; }

    /// <summary>Plain-language explanation for citizens ("Pays for road repair from state gas tax").</summary>
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public FundGroup Group => Category.ToGroup();

    public Fund(Guid governmentId, string code, string name, FundCategory category, string? description = null)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        GovernmentId = governmentId;
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        Category = category;
        SetDescription(description);
    }

    private Fund()
    {
        Code = null!;
        Name = null!;
    }

    public void Update(string code, string name, FundCategory category, string? description)
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        Category = category;
        SetDescription(description);
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    /// <summary>
    /// Funds are retired, never deleted: last year's budget and every published snapshot still point
    /// at them. A retired fund drops out of pick lists and new budgets.
    /// </summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
