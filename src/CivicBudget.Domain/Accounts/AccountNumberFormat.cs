using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Accounts;

/// <summary>
/// How a government writes a full account number. Ohio charts compose it from the same three
/// codes this model already stores: fund, then the program or department, then the object
/// (the account). A UAN village writes "1000-725-121"; a county ERP might write "101.110.5100".
/// Revenue lines usually have no middle segment: "1000-110". The widths are what the government
/// pads codes to when it writes them, and what the parser uses when a number arrives without
/// separators; the label is the word the government uses for the middle segment.
/// Owned by <see cref="Governments.Government"/> and copied into published snapshots.
/// </summary>
public sealed record AccountNumberFormat
{
    public const int MinWidth = 1;
    public const int MaxWidth = 8;

    public static readonly AccountNumberFormat UanVillage = new(4, 3, 4, "-", "Program");
    public static readonly AccountNumberFormat County = new(3, 3, 4, "-", "Department");

    public int FundWidth { get; }
    public int DepartmentWidth { get; }
    public int ObjectWidth { get; }

    /// <summary>One character between segments: "-" or "." in practice; never a digit.</summary>
    public string Separator { get; }

    /// <summary>"Program" (UAN) or "Department" (most county and city charts).</summary>
    public string DepartmentLabel { get; }

    public AccountNumberFormat(int fundWidth, int departmentWidth, int objectWidth, string separator, string departmentLabel)
    {
        FundWidth = ValidateWidth(fundWidth, nameof(fundWidth));
        DepartmentWidth = ValidateWidth(departmentWidth, nameof(departmentWidth));
        ObjectWidth = ValidateWidth(objectWidth, nameof(objectWidth));
        Guard.Against(separator.Length != 1 || char.IsLetterOrDigit(separator[0]), "The separator must be one punctuation character.");
        Separator = separator;
        DepartmentLabel = Guard.MaxLength(Guard.NotNullOrWhiteSpace(departmentLabel, nameof(departmentLabel)), 20, nameof(departmentLabel));
    }

    private AccountNumberFormat()
    {
        Separator = null!;
        DepartmentLabel = null!;
    }

    private static int ValidateWidth(int width, string name)
    {
        Guard.Against(width is < MinWidth or > MaxWidth, $"{name} must be between {MinWidth} and {MaxWidth}.");
        return width;
    }
}
