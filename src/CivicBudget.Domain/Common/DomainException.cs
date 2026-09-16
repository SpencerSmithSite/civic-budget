namespace CivicBudget.Domain.Common;

/// <summary>
/// Thrown when a domain invariant would be violated (e.g. editing an adopted budget).
/// These are programming errors or bypass attempts, not user-input problems;
/// user-input validation returns results instead of throwing.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException()
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
