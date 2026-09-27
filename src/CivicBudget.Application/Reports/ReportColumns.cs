using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

/// <summary>
/// The rules every report's columns follow, wherever they are saved. Columns are sets of accounts
/// shown side by side with a last column for everything else, so an account in two columns would be
/// counted twice, and an account of the wrong kind would be counted where it does not belong.
/// </summary>
public static class ReportColumnRules
{
    /// <summary>Room on a landscape page for the label, this many columns, the last column, and the total.</summary>
    public const int MaxColumns = 4;

    public static List<string> Problems(IReadOnlyList<ReportColumnDto> columns, string? otherLabel, IReadOnlySet<Guid> eligible, string kindOfAccount)
    {
        var problems = new List<string>();
        if (columns.Count > MaxColumns)
        {
            problems.Add($"A report has room for {MaxColumns} columns before {otherLabel}.");
        }

        var labels = columns.Select(c => c.Label?.Trim() ?? "").Append(otherLabel?.Trim() ?? "").ToList();
        if (labels.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("Every column needs a heading.");
        }
        else if (labels.GroupBy(l => l, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            problems.Add($"Two columns are headed \"{duplicate.Key}\"; give each its own heading.");
        }

        foreach (ReportColumnDto column in columns.Where(c => c.AccountIds.Any(id => !eligible.Contains(id))))
        {
            problems.Add($"{column.Label}: only {kindOfAccount} accounts can be in this column.");
        }

        var twice = columns.SelectMany(c => c.AccountIds.Distinct()).GroupBy(id => id).Where(g => g.Count() > 1).ToList();
        if (twice.Count > 0)
        {
            problems.Add($"{twice.Count} account{(twice.Count == 1 ? " is" : "s are")} in more than one column, which would count the money twice.");
        }

        return problems;
    }
}

/// <summary>The appropriation measure's columns and the expenditure accounts that can go in them.</summary>
public sealed record MeasureColumnsDto(IReadOnlyList<ReportColumnDto> Columns, bool UsingDefaultColumns, IReadOnlyList<ReportAccountDto> ExpenditureAccounts);

/// <summary>
/// The appropriation measure's columns: expenditure accounts grouped as the legislative authority
/// appropriates them. ORC 5705.38(C) asks for personal services separately within each office and
/// department; everything else is "Other". Administrators and the Fiscal Officer change them.
/// </summary>
public interface IMeasureColumnService
{
    Task<MeasureColumnsDto> GetAsync(CancellationToken ct = default);

    Task<Result> SaveAsync(IReadOnlyList<ReportColumnDto> columns, CancellationToken ct = default);
}

public sealed class MeasureColumnService(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser, TimeProvider clock) : IMeasureColumnService
{
    public const string OtherLabel = "Other";
    public const string DefaultLabel = "Personal services";

    public async Task<MeasureColumnsDto> GetAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (List<(string Label, HashSet<Guid> Accounts)> columns, bool isDefault) = await ColumnsAsync(db, ct);
        List<ReportAccountDto> accounts = await db.Accounts.Where(a => a.Type == AccountType.Expenditure).OrderBy(a => a.Code)
            .Select(a => new ReportAccountDto(a.Id, a.Code, a.Name, a.Category, a.IsActive)).ToListAsync(ct);
        return new MeasureColumnsDto(columns.Select(c => new ReportColumnDto(c.Label, accounts.Where(a => c.Accounts.Contains(a.Id)).Select(a => a.Id).ToList())).ToList(),
            isDefault, accounts);
    }

    public async Task<Result> SaveAsync(IReadOnlyList<ReportColumnDto> columns, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure("Only an Administrator or the Fiscal Officer can change the appropriation measure's columns.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        HashSet<Guid> expenditure = (await db.Accounts.Where(a => a.Type == AccountType.Expenditure).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        if (ReportColumnRules.Problems(columns, OtherLabel, expenditure, "expenditure") is { Count: > 0 } problems)
        {
            return Result.Failure(problems.Select(p => new ValidationError(string.Empty, p)));
        }

        Guid governmentId = currentUser.GovernmentId!.Value;
        db.ReportAccountGroups.RemoveRange(await db.ReportAccountGroups.Where(g => g.Report == ReportKind.AppropriationMeasure).ToListAsync(ct));
        for (int i = 0; i < columns.Count; i++)
        {
            db.ReportAccountGroups.Add(new ReportAccountGroup(governmentId, ReportKind.AppropriationMeasure, columns[i].Label, i, columns[i].AccountIds));
        }

        db.AuditEntries.Add(AuditEntry.Event(governmentId, nameof(Government), governmentId,
            $"Updated the appropriation measure's columns: {string.Join(", ", columns.Select(c => $"{c.Label.Trim()} ({c.AccountIds.Count} account{(c.AccountIds.Count == 1 ? "" : "s")})").Append(OtherLabel))}",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// The saved columns, or the default: one "Personal services" column of the accounts categorized as
    /// personal services or fringe benefits, the pay and the benefits that come with it.
    /// </summary>
    public static async Task<(List<(string Label, HashSet<Guid> Accounts)> Columns, bool IsDefault)> ColumnsAsync(ICivicBudgetDbContext db, CancellationToken ct)
    {
        List<ReportAccountGroup> groups = await db.ReportAccountGroups.Include(g => g.Accounts)
            .Where(g => g.Report == ReportKind.AppropriationMeasure).OrderBy(g => g.SortOrder).ToListAsync(ct);
        if (groups.Count > 0)
        {
            return (groups.Select(g => (g.Label, g.Accounts.Select(a => a.AccountId).ToHashSet())).ToList(), false);
        }

        HashSet<Guid> personal = (await db.Accounts
            .Where(a => a.Type == AccountType.Expenditure && (a.Category == ReportingCategory.PersonalServices || a.Category == ReportingCategory.FringeBenefits))
            .Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        return ([(DefaultLabel, personal)], true);
    }
}
