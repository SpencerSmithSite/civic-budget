using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Publishing;

public sealed class PublishingService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock,
    IPublishedSnapshotCacheInvalidator cacheInvalidator) : IPublishingService
{
    private const string NotAllowed = "Only the Finance Director can publish or unpublish a budget.";

    public async Task<IReadOnlyList<SnapshotSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PublishedBudgetSnapshots
            .OrderByDescending(s => s.FiscalYear).ThenByDescending(s => s.PublishedAtUtc)
            .Select(s => new SnapshotSummaryDto(
                s.Id, s.BudgetVersionId, s.FiscalYear, s.VersionLabel, s.Status, s.PublishedAtUtc, s.PublishedByUserName,
                s.StatusChangedAtUtc, s.Lines.Count))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> PublishAsync(Guid versionId, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.FinanceDirector))
        {
            return Result.Failure<Guid>(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.BeginningBalances)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return Result.Failure<Guid>("Budget version was not found.");
        }

        if (await db.PublishedBudgetSnapshots.AnyAsync(s => s.BudgetVersionId == version.Id && s.Status == SnapshotStatus.Active, ct))
        {
            return Result.Failure<Guid>("This version is already published.");
        }

        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        FiscalYear fiscalYear = await db.FiscalYears.SingleAsync(fy => fy.Id == version.FiscalYearId, ct);
        List<Fund> funds = await db.Funds.ToListAsync(ct); // includes inactive funds, which may still carry lines

        PublishedBudgetSnapshot snapshot;
        try
        {
            snapshot = PublishedBudgetSnapshot.Capture(government, fiscalYear, version, funds,
                currentUser.UserId!, currentUser.DisplayName ?? currentUser.UserId!, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }

        // Exactly one active snapshot per fiscal year: the previous one becomes history.
        PublishedBudgetSnapshot? previous = await db.PublishedBudgetSnapshots
            .FirstOrDefaultAsync(s => s.FiscalYear == snapshot.FiscalYear && s.Status == SnapshotStatus.Active, ct);
        if (previous is not null)
        {
            previous.MarkSuperseded(snapshot, currentUser.UserId!, clock.GetUtcNow());
            db.AuditEntries.Add(Event(previous, $"Superseded by publication of {snapshot.VersionLabel}"));
        }

        db.PublishedBudgetSnapshots.Add(snapshot);
        db.AuditEntries.Add(Event(snapshot, $"Published FY{snapshot.FiscalYear} {snapshot.VersionLabel} ({snapshot.Lines.Count} lines)"));
        db.AuditEntries.Add(AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id, "Published to the public portal",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        await cacheInvalidator.InvalidateAsync(government.PublicSlug, ct);
        return Result.Success(snapshot.Id);
    }

    public async Task<Result> UnpublishAsync(Guid snapshotId, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.FinanceDirector))
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        PublishedBudgetSnapshot? snapshot = await db.PublishedBudgetSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
        if (snapshot is null)
        {
            return Result.Failure("Snapshot was not found.");
        }

        try
        {
            snapshot.Unpublish(currentUser.UserId!, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        db.AuditEntries.Add(Event(snapshot, $"Unpublished FY{snapshot.FiscalYear} {snapshot.VersionLabel}"));
        await db.SaveChangesAsync(ct);

        await cacheInvalidator.InvalidateAsync(snapshot.GovernmentSlug, ct);
        return Result.Success();
    }

    private AuditEntry Event(PublishedBudgetSnapshot snapshot, string description) =>
        AuditEntry.Event(snapshot.GovernmentId, nameof(PublishedBudgetSnapshot), snapshot.Id, description,
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow());
}
