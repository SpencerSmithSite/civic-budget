using CivicBudget.Application.Security;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>One demo login per role for Maple Ridge, plus an admin for Pine Hollow. The .example domain is reserved and never routes.</summary>
internal sealed record DemoUser(string Email, string DisplayName, string Role, params string[] DepartmentCodes);

internal static class DemoUsers
{
    public static readonly IReadOnlyList<DemoUser> MapleRidge =
    [
        new("admin@mapleridge.example", "Alex Rivera (Admin)", Roles.Admin),
        new("finance@mapleridge.example", "Dana Whitfield (Fiscal Officer)", Roles.FinanceDirector),
        new("police@mapleridge.example", "Chief Morgan Hale", Roles.DepartmentHead, "110"),
        new("streets@mapleridge.example", "Sam Okafor (Service Director)", Roles.DepartmentHead, "620", "310"),
        new("viewer@mapleridge.example", "Council Member Lee", Roles.Viewer),
    ];

    public static readonly IReadOnlyList<DemoUser> PineHollow =
    [
        // A small township's fiscal officer often runs the whole system, so this login is an Administrator.
        new("admin@pinehollow.example", "Jordan Blake (Fiscal Officer)", Roles.Admin),
    ];
}
