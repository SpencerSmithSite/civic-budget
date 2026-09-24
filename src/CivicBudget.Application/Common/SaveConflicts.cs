using CivicBudget.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Common;

/// <summary>
/// Saving a budget change when someone else changed the same budget first. The version's revision
/// (a concurrency token) makes the database refuse a save based on a stale read, and two people
/// creating the same thing at once (two amendments, two first narratives) collide on a unique index.
/// Either way the user gets a message and the other person's change stands; nothing is overwritten.
/// </summary>
public static class SaveConflicts
{
    public const string Message = "Someone else changed this budget at the same moment, so your change was not saved. Reload the page to see the current budget, then try again.";

    /// <summary>Saves, or returns the conflict as a failed result. Null means saved.</summary>
    /// <remarks>
    /// Every other rule is checked before these saves (inputs, permissions, the domain's guards), so an
    /// update exception here is the race and not a bug to be hidden; unique indexes are the only
    /// constraints these writes can still hit.
    /// </remarks>
    public static async Task<Result?> TrySaveAsync(this ICivicBudgetDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            // DbUpdateConcurrencyException derives from DbUpdateException: stale revision or duplicate key.
            return Result.Failure(Message);
        }
    }
}
