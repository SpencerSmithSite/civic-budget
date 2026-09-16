namespace CivicBudget.Application.Tenancy;

/// <summary>
/// Raised when code tries to write a row that belongs to a different government than the current
/// tenant, or to write tenant-owned data with no tenant set. This is never a user-input error; it is
/// a bug or an attack, so it is logged loudly and not shown as a validation message.
/// </summary>
public sealed class TenantIsolationException : Exception
{
    public TenantIsolationException(string message) : base(message)
    {
    }

    public TenantIsolationException()
    {
    }

    public TenantIsolationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
