namespace CivicBudget.Domain.Accounts;

public enum AccountType
{
    Revenue = 1,
    Expenditure = 2,
    TransferIn = 3,
    TransferOut = 4,
}

public static class AccountTypeExtensions
{
    /// <summary>Resources: money coming into the fund (counts toward estimated resources).</summary>
    public static bool IsResource(this AccountType type) =>
        type is AccountType.Revenue or AccountType.TransferIn;

    /// <summary>Appropriations: authority to spend out of the fund (limited by estimated resources).</summary>
    public static bool IsAppropriation(this AccountType type) =>
        type is AccountType.Expenditure or AccountType.TransferOut;
}
