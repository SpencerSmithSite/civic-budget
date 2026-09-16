namespace CivicBudget.Domain.Common;

/// <summary>Small set of argument checks used by entity constructors and methods.</summary>
public static class Guard
{
    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{paramName} is required.");
        }

        return value.Trim();
    }

    public static string MaxLength(string value, int maxLength, string paramName)
    {
        if (value.Length > maxLength)
        {
            throw new DomainException($"{paramName} must be {maxLength} characters or fewer.");
        }

        return value;
    }

    public static void Against(bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(message);
        }
    }
}
