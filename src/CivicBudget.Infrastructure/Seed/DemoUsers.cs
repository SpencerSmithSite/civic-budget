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
        new("police@mapleridge.example", "Chief Morgan Hale", Roles.DepartmentHead, "PD"),
        new("streets@mapleridge.example", "Sam Okafor (Service Director)", Roles.DepartmentHead, "ST", "PR"),
        new("viewer@mapleridge.example", "Council Member Lee", Roles.Viewer),
    ];

    public static readonly IReadOnlyList<DemoUser> PineHollow =
    [
        new("admin@pinehollow.example", "Jordan Blake (Fiscal Officer)", Roles.Admin),
    ];
}
