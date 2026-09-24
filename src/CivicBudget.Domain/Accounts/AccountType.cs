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

    /// <summary>
    /// What a department's (office's, program's) budget total is: its appropriations for expenditure,
    /// and nothing else. Ohio appropriates by fund, then by office, department, and division, with
    /// personal services shown within each (ORC 5705.38(C)). Transfers out are appropriated too, but
    /// as the fund's "other financing uses", under their own program in the UAN chart (910) rather
    /// than inside an operating department; Michigan's uniform chart does the same (activity 965).
    /// Revenue a department collects (court fines, permit fees) is an estimated resource of the fund
    /// and is never part of any appropriation. Screens still show those lines beside the department;
    /// they are simply outside its total.
    /// </summary>
    public static bool CountsTowardDepartmentTotal(this AccountType type) => type == AccountType.Expenditure;
}
