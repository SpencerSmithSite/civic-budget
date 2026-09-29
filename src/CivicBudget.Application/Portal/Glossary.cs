namespace CivicBudget.Application.Portal;

public sealed record GlossaryTerm(string Term, string Definition);

/// <summary>
/// The words the budget uses the way Ohio law does, in plain language. One list for the portal's
/// glossary page and the budget book's, so a resident reads the same definition in both.
/// </summary>
public static class Glossary
{
    public static IReadOnlyList<GlossaryTerm> Terms { get; } =
    [
        new("Fund", "A separate set of books for money that can only be used for a purpose: the General Fund for day-to-day services, the Street fund for roads, the Water fund for the water system."),
        new("Appropriation", "Council's legal permission to spend up to an amount. Departments cannot spend money that has not been appropriated."),
        new("Estimated resources", "What a fund starts the year with plus what it expects to take in. In Ohio, appropriations may not exceed this amount."),
        new("Beginning balance", "The money a fund carries in from the year before. Part of its estimated resources."),
        new("Projected ending balance", "Estimated resources less appropriations: what the fund expects to carry into next year if the budget plays out as planned."),
        new("Transfer", "Money moved from one fund to another by council action, for example from the General Fund to a capital projects fund. It is counted once, as a transfer, not as revenue or spending."),
        new("Department", "The office or service that spends the money: Police, Streets & Service, Parks & Recreation. A department may spend from more than one fund."),
        new("Account number", "The code on each line, written fund, then department, then object (the kind of spending), for example 1000-110-5110 for General Fund, Police, Salaries & Wages. Revenue lines skip the department."),
        new("Amendment", "A change to the adopted budget passed by council during the year, usually to move or add money for something unexpected. Each amendment replaces the version before it."),
        new("Resolution", "The numbered act of council that adopted the budget or an amendment. Its number finds it in the council minutes."),
    ];
}
