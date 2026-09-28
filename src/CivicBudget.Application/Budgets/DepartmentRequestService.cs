using CivicBudget.Application.Common;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Budgets;

public sealed class DepartmentRequestService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IEmailOutbox outbox,
    IUserDirectory users,
    IAppLinks links,
    TimeProvider clock) : IDepartmentRequestService
{
    public async Task<Result> SaveNarrativeAsync(Guid versionId, Guid departmentId, string? narrative, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion? version, Department? department) = await LoadAsync(db, versionId, departmentId, ct);
        if (version is null || department is null)
        {
            return Result.Failure("Budget version or department was not found.");
        }

        if (!BudgetLinePermissions.CanEditNarrative(currentUser, version.Status, department.Id, version.IsDepartmentSubmitted(department.Id)))
        {
            return Result.Failure("You do not have permission to change this department's narrative.");
        }

        try
        {
            version.SetDepartmentNarrative(department, narrative);
        }
        catch (DomainException ex)
        {
            return Result.Failure(nameof(narrative), ex.Message);
        }

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        return Result.Success();
    }

    public async Task<Result> SubmitAsync(Guid versionId, Guid departmentId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion? version, Department? department) = await LoadAsync(db, versionId, departmentId, ct);
        if (version is null || department is null)
        {
            return Result.Failure("Budget version or department was not found.");
        }

        DepartmentRequestStatus status = version.GetDepartmentRequest(department.Id)?.Status ?? DepartmentRequestStatus.InProgress;
        if (!BudgetLinePermissions.CanSubmitDepartment(currentUser, version.Status, department.Id, status))
        {
            return Result.Failure("You do not have permission to submit this department's request.");
        }

        try
        {
            version.SubmitDepartment(department, currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        db.AuditEntries.Add(Event(version, $"{department.Name} submitted its budget request"));

        // The fiscal officer hears about it, in the same save: no submission without its notice. An
        // officer who submits on a department's behalf is not told about their own action.
        (string government, string budget) = await NamesAsync(db, version, ct);
        EmailContent notice = EmailTemplates.DepartmentSubmitted(government, department.Name, budget, currentUser.DisplayName ?? "A department user",
            links.Absolute($"admin/budgets/{version.Id}/departments/{department.Id}"));
        foreach (EmailRecipient officer in (await users.FiscalAuthorityAsync(version.GovernmentId, ct)).Where(r => r.UserId != currentUser.UserId))
        {
            outbox.Add(db, version.GovernmentId, EmailKind.DepartmentSubmitted, officer, notice);
        }

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        outbox.Notify();
        return Result.Success();
    }

    public async Task<Result> ReturnAsync(Guid versionId, Guid departmentId, string note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Result.Failure(nameof(note), "Tell the department what to change before returning its request.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion? version, Department? department) = await LoadAsync(db, versionId, departmentId, ct);
        if (version is null || department is null)
        {
            return Result.Failure("Budget version or department was not found.");
        }

        DepartmentRequestStatus status = version.GetDepartmentRequest(department.Id)?.Status ?? DepartmentRequestStatus.InProgress;
        if (!BudgetLinePermissions.CanReturnDepartment(currentUser, version.Status, status))
        {
            return Result.Failure("Only an Administrator or the Fiscal Officer can return a submitted request.");
        }

        try
        {
            version.ReturnDepartment(department, note, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        db.AuditEntries.Add(Event(version, $"Returned {department.Name}'s budget request: {note.Trim()}"));

        (string government, string budget) = await NamesAsync(db, version, ct);
        EmailContent notice = EmailTemplates.DepartmentReturned(government, department.Name, budget, note.Trim(),
            links.Absolute($"admin/budgets/{version.Id}/departments/{department.Id}"));
        foreach (EmailRecipient member in await users.DepartmentUsersAsync(version.GovernmentId, department.Id, ct))
        {
            outbox.Add(db, version.GovernmentId, EmailKind.DepartmentReturned, member, notice);
        }

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        outbox.Notify();
        return Result.Success();
    }

    /// <summary>Lines are needed because submitting requires the department to have some; the tenant filter scopes both lookups.</summary>
    private static async Task<(BudgetVersion? Version, Department? Department)> LoadAsync(ICivicBudgetDbContext db, Guid versionId, Guid departmentId, CancellationToken ct)
    {
        BudgetVersion? version = await db.BudgetVersions
            .Include(v => v.Lines)
            .Include(v => v.DepartmentRequests)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        return (version, department);
    }

    private static async Task<(string Government, string Budget)> NamesAsync(ICivicBudgetDbContext db, BudgetVersion version, CancellationToken ct)
    {
        string government = await db.Governments.Where(g => g.Id == version.GovernmentId).Select(g => g.Name).SingleAsync(ct);
        int year = await db.FiscalYears.Where(f => f.Id == version.FiscalYearId).Select(f => f.Year).SingleAsync(ct);
        return (government, $"FY{year} {version.Label}");
    }

    private AuditEntry Event(BudgetVersion version, string description) =>
        AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id, description,
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow());
}
