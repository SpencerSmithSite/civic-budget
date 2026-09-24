using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Departments;

/// <summary>
/// An organizational unit (Police, Streets &amp; Service, Water Utility). Departments are cross-fund:
/// one department may have budget lines in several funds.
/// </summary>
[Audited]
public sealed class Department : Entity, ITenantOwned
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    public Guid GovernmentId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Department(Guid governmentId, string code, string name, string? description = null)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        GovernmentId = governmentId;
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        SetDescription(description);
    }

    private Department()
    {
        Code = null!;
        Name = null!;
    }

    public void Update(string code, string name, string? description)
    {
        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code, nameof(code)), CodeMaxLength, nameof(code));
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        SetDescription(description);
    }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    /// <summary>Retired rather than deleted, for the same reason as <see cref="Funds.Fund.Deactivate"/>.</summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
