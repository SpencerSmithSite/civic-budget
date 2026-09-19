using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Erp;

public enum ChartChangeKind
{
    /// <summary>In the ERP, not yet here: will be added.</summary>
    Add = 1,

    /// <summary>Here and in the ERP with a different name, category, type, or description: will be updated.</summary>
    Update = 2,

    /// <summary>Here but no longer in the ERP (or inactive there): will be deactivated, never deleted.</summary>
    Deactivate = 3,

    /// <summary>Inactive here, active in the ERP again.</summary>
    Reactivate = 4,

    /// <summary>Identical: nothing to do.</summary>
    Unchanged = 5,
}

/// <summary>One code's fate in a sync, with the before and after a person needs to judge it.</summary>
public sealed record ChartChange(string Kind, string Code, ChartChangeKind Change, string? Before, string After);

/// <summary>What a sync would do, for the preview screen and the audit event.</summary>
public sealed record ChartSyncPreviewDto(string SourceName, string FileName, IReadOnlyList<ChartChange> Changes, AccountNumberFormat? NumberFormat)
{
    public int Count(ChartChangeKind kind) => Changes.Count(c => c.Change == kind);
    public bool HasChanges => Changes.Any(c => c.Change != ChartChangeKind.Unchanged);
}

/// <summary>The codes CivicBudget holds today, as the differ sees them.</summary>
public sealed record LocalFund(Guid Id, string Code, string Name, FundCategory Category, string? Description, bool IsActive);
public sealed record LocalDepartment(Guid Id, string Code, string Name, string? Description, bool IsActive);
public sealed record LocalObject(Guid Id, string Code, string Name, AccountType Type, ReportingCategory Category, bool IsActive);

/// <summary>
/// Compares the ERP's chart with what CivicBudget holds and says what a sync would do. Pure, so
/// every rule has a unit test. Codes match case-insensitively; a code missing from the ERP is
/// deactivated rather than deleted because budget lines and snapshots still point at it.
/// </summary>
public static class ChartDiff
{
    public static IReadOnlyList<ChartChange> Compute(ErpChart erp, IReadOnlyList<LocalFund> funds, IReadOnlyList<LocalDepartment> departments, IReadOnlyList<LocalObject> objects)
    {
        var changes = new List<ChartChange>();

        Diff(changes, "Fund", erp.Funds.Select(f => (f.Code, f.IsActive, Describe(f))), funds.Select(f => (f.Code, f.IsActive, Describe(f))));
        Diff(changes, "Department", erp.Departments.Select(d => (d.Code, d.IsActive, Describe(d))), departments.Select(d => (d.Code, d.IsActive, Describe(d))));
        Diff(changes, "Object", erp.Objects.Select(o => (o.Code, o.IsActive, Describe(o))), objects.Select(o => (o.Code, o.IsActive, Describe(o))));

        return changes;
    }

    private static void Diff(List<ChartChange> changes, string kind, IEnumerable<(string Code, bool IsActive, string Text)> erp, IEnumerable<(string Code, bool IsActive, string Text)> local)
    {
        Dictionary<string, (string Code, bool IsActive, string Text)> localByCode = local.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string code, bool erpActive, string erpText) in erp)
        {
            seen.Add(code);
            if (!localByCode.TryGetValue(code, out (string Code, bool IsActive, string Text) mine))
            {
                changes.Add(new ChartChange(kind, code, erpActive ? ChartChangeKind.Add : ChartChangeKind.Unchanged, null, erpText));
                continue;
            }

            if (!erpActive)
            {
                changes.Add(new ChartChange(kind, code, mine.IsActive ? ChartChangeKind.Deactivate : ChartChangeKind.Unchanged, mine.Text, erpText));
            }
            else if (!mine.IsActive)
            {
                changes.Add(new ChartChange(kind, code, ChartChangeKind.Reactivate, mine.Text, erpText));
            }
            else
            {
                changes.Add(new ChartChange(kind, code, mine.Text == erpText ? ChartChangeKind.Unchanged : ChartChangeKind.Update, mine.Text, erpText));
            }
        }

        // Codes the ERP no longer lists: gone from budgeting, kept for history.
        foreach ((string code, bool isActive, string text) in localByCode.Values.Where(l => !seen.Contains(l.Code) && l.IsActive))
        {
            changes.Add(new ChartChange(kind, code, ChartChangeKind.Deactivate, text, "(not in the ERP)"));
        }
    }

    // One string per code so "changed" is one comparison and the preview can show before and after.
    private static string Describe(ErpFund f) => $"{f.Name} · {f.Category}{Suffix(f.Description)}";
    private static string Describe(LocalFund f) => $"{f.Name} · {f.Category}{Suffix(f.Description)}";
    private static string Describe(ErpDepartment d) => d.Name + Suffix(d.Description);
    private static string Describe(LocalDepartment d) => d.Name + Suffix(d.Description);
    private static string Describe(ErpObject o) => $"{o.Name} · {o.Type} · {o.Category}";
    private static string Describe(LocalObject o) => $"{o.Name} · {o.Type} · {o.Category}";
    private static string Suffix(string? description) => string.IsNullOrWhiteSpace(description) ? "" : " · " + description.Trim();
}
