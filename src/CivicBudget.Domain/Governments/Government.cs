using System.Text.RegularExpressions;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Governments;

/// <summary>
/// The tenant. Every other entity in the model belongs to exactly one Government.
/// Not itself <see cref="ITenantOwned"/>, because it *is* the tenant.
/// </summary>
public sealed partial class Government : Entity
{
    public const int NameMaxLength = 200;
    public const int SlugMaxLength = 60;

    public string Name { get; private set; }
    public GovernmentType Type { get; private set; }

    /// <summary>Two-letter US state code, e.g. "OH".</summary>
    public string State { get; private set; }

    /// <summary>1 to 12. Most Ohio subdivisions use 1 (calendar year); some use 7 (July to June).</summary>
    public int FiscalYearStartMonth { get; private set; }

    /// <summary>URL segment for the public portal: lowercase letters, digits, hyphens.</summary>
    public string PublicSlug { get; private set; }

    public AppropriationLimitMode AppropriationLimitMode { get; private set; }

    /// <summary>Plain-language introduction shown to citizens on the portal.</summary>
    public string? Description { get; private set; }

    public Government(
        string name,
        GovernmentType type,
        string state,
        int fiscalYearStartMonth,
        string publicSlug,
        AppropriationLimitMode appropriationLimitMode = AppropriationLimitMode.Block)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));
        Type = type;
        State = ValidateState(state);
        FiscalYearStartMonth = ValidateMonth(fiscalYearStartMonth);
        PublicSlug = ValidateSlug(publicSlug);
        AppropriationLimitMode = appropriationLimitMode;
    }

    // EF Core materializes entities through this constructor; it never runs domain validation.
    private Government()
    {
        Name = null!;
        State = null!;
        PublicSlug = null!;
    }

    public void Rename(string name) =>
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, nameof(name)), NameMaxLength, nameof(name));

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void SetAppropriationLimitMode(AppropriationLimitMode mode) => AppropriationLimitMode = mode;

    public void SetFiscalYearStartMonth(int month) => FiscalYearStartMonth = ValidateMonth(month);

    public void SetPublicSlug(string slug) => PublicSlug = ValidateSlug(slug);

    private static string ValidateState(string state)
    {
        string value = Guard.NotNullOrWhiteSpace(state, nameof(state)).ToUpperInvariant();
        Guard.Against(value.Length != 2 || !value.All(char.IsAsciiLetterUpper), "State must be a two-letter code.");
        return value;
    }

    private static int ValidateMonth(int month)
    {
        Guard.Against(month is < 1 or > 12, "Fiscal year start month must be between 1 and 12.");
        return month;
    }

    private static string ValidateSlug(string slug)
    {
        string value = Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug, nameof(slug)), SlugMaxLength, nameof(slug));
        Guard.Against(!SlugPattern().IsMatch(value), "Public slug may contain only lowercase letters, digits, and single hyphens.");
        return value;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
