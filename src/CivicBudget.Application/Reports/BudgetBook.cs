using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Reports;

namespace CivicBudget.Application.Reports;

/// <summary>The four sections a book may leave out. The rest (cover, contents, message, summary, funds, departments, certificate) is always there.</summary>
public sealed record BudgetBookOptions(bool Outlook, bool Personnel, bool LineItems, bool Glossary)
{
    public static BudgetBookOptions From(BudgetBookSettings? settings) => settings is null
        ? From(new BudgetBookSettings(Guid.Empty))
        : new(settings.IncludeOutlook, settings.IncludePersonnel, settings.IncludeLineItems, settings.IncludeGlossary);
}

/// <summary>What the cover says: whose budget, which year and version, and whether council has adopted it.</summary>
public sealed record BookCoverDto(
    string GovernmentName,
    int FiscalYear,
    string VersionLabel,
    BudgetStatus Status,
    string? ResolutionNumber,
    DateOnly? AdoptedOn,
    string? AmendmentReason,
    byte[]? Logo,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc)
{
    /// <summary>A book of a budget council has not adopted says so on every page.</summary>
    public bool IsDraft => Status != BudgetStatus.Adopted;
}

/// <summary>The budget message, split into paragraphs at blank lines the way it was typed.</summary>
public sealed record BookMessageDto(string Heading, IReadOnlyList<string> Paragraphs, string? SignedBy, string? SignerTitle);

/// <summary>A row of a book table: a label and the year before, this year, and the new budget.</summary>
public sealed record BookAmountDto(string Label, decimal PriorYearActual, decimal CurrentYearBudget, decimal Amount)
{
    public decimal Change => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>A fund's page: what it is for, its limit arithmetic, where its money comes from, and who spends it.</summary>
public sealed record BookFundDto(
    string Code,
    string Name,
    FundCategory? Category,
    string? Description,
    FundSummaryRowDto Summary,
    IReadOnlyList<BookAmountDto> Resources,
    IReadOnlyList<BookAmountDto> Uses);

/// <summary>A department's page: its narrative and its spending by kind, across every fund it spends from.</summary>
public sealed record BookDepartmentDto(string Code, string Name, string? Narrative, IReadOnlyList<string> Funds, IReadOnlyList<BookAmountDto> Categories)
{
    public BookAmountDto Total => new("Total", Categories.Sum(c => c.PriorYearActual), Categories.Sum(c => c.CurrentYearBudget), Categories.Sum(c => c.Amount));
}

/// <summary>One fund and department's lines in the line-item appendix.</summary>
public sealed record BookLineGroupDto(string Title, IReadOnlyList<BookLineDto> Lines);

public sealed record BookLineDto(string AccountNumber, string AccountName, decimal PriorYearActual, decimal CurrentYearBudget, decimal Amount)
{
    public decimal Change => Amount - CurrentYearBudget;
}

/// <summary>
/// The whole book with every number worked out and every section decided, so the renderer only
/// draws. A section left out by the options is null.
/// </summary>
public sealed record BudgetBookDto(
    BookCoverDto Cover,
    BookMessageDto? Message,
    FundSummaryReportDto FundSummary,
    CategoryReportDto Categories,
    IReadOnlyList<BookFundDto> Funds,
    IReadOnlyList<BookDepartmentDto> Departments,
    CertificateReportDto? Certificate,
    BudgetPlanDto? Outlook,
    PersonnelCostDto? Personnel,
    IReadOnlyList<BookLineGroupDto>? LineItems,
    IReadOnlyList<GlossaryTerm>? Glossary);

/// <summary>The parts the book is assembled from, each as its own report already produces it.</summary>
public sealed record BudgetBookSources(
    BookCoverDto Cover,
    string? MessageHeading,
    string? MessageBody,
    string? MessageSignedBy,
    string? MessageSignerTitle,
    FundSummaryReportDto FundSummary,
    CategoryReportDto Categories,
    DepartmentDetailReportDto DepartmentDetail,
    IReadOnlyList<BudgetLineDto> Lines,
    IReadOnlyDictionary<string, string?> FundDescriptions,
    CertificateReportDto? Certificate,
    BudgetPlanDto? Plan,
    PersonnelCostDto? Personnel);

/// <summary>
/// Assembles a budget book from reports that already exist, so every figure in the book is the
/// figure on the matching screen. Pure: the service gathers the sources, this decides what the
/// pages say, and the tests check it without a database or a PDF.
/// </summary>
public static class BudgetBookBuilder
{
    public const string DefaultMessageHeading = "Budget message";
    public const string FundLevel = "Fund-level (no department)";

    public static BudgetBookDto Build(BudgetBookSources s, BudgetBookOptions options) => new(
        s.Cover,
        Message(s),
        s.FundSummary,
        s.Categories,
        [.. s.FundSummary.Funds.Select(f => Fund(f, s.Lines.Where(l => l.FundCode == f.FundCode).ToList(), s.FundDescriptions.GetValueOrDefault(f.FundCode)))],
        [.. s.DepartmentDetail.Departments.OrderBy(d => d.DepartmentCode).Select(Department)],
        s.Certificate,
        options.Outlook && s.Plan is { Years: > 1 } ? s.Plan : null,
        options.Personnel && s.Personnel is { HasSettings: true, Funds.Count: > 0 } ? s.Personnel : null,
        options.LineItems ? LineItems(s.Lines) : null,
        options.Glossary ? Glossary.Terms : null);

    private static BookMessageDto? Message(BudgetBookSources s) =>
        string.IsNullOrWhiteSpace(s.MessageBody)
            ? null
            : new BookMessageDto(
                s.MessageHeading ?? DefaultMessageHeading,
                [.. s.MessageBody.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                s.MessageSignedBy,
                s.MessageSignerTitle);

    /// <summary>
    /// Resources by source (revenue categories, then transfers in) and uses by department (with the
    /// fund's own lines, such as debt service kept at the fund, and transfers out, as their own rows).
    /// </summary>
    private static BookFundDto Fund(FundSummaryRowDto summary, List<BudgetLineDto> lines, string? description)
    {
        List<BookAmountDto> resources = [.. lines.Where(l => l.AccountType == AccountType.Revenue)
            .GroupBy(l => l.Category).OrderBy(g => g.Key)
            .Select(g => Row(Labels.Category(g.Key), g))];
        if (lines.Any(l => l.AccountType == AccountType.TransferIn))
        {
            resources.Add(Row("Transfers in", lines.Where(l => l.AccountType == AccountType.TransferIn)));
        }

        List<BookAmountDto> uses = [.. lines.Where(l => l.AccountType == AccountType.Expenditure)
            .GroupBy(l => (l.DepartmentCode, l.DepartmentName)).OrderBy(g => g.Key.DepartmentCode is null).ThenBy(g => g.Key.DepartmentCode)
            .Select(g => Row(g.Key.DepartmentCode is null ? FundLevel : $"{g.Key.DepartmentCode} {g.Key.DepartmentName}", g))];
        if (lines.Any(l => l.AccountType == AccountType.TransferOut))
        {
            uses.Add(Row("Transfers out", lines.Where(l => l.AccountType == AccountType.TransferOut)));
        }

        return new BookFundDto(summary.FundCode, summary.FundName, summary.Category, description, summary, resources, uses);
    }

    // A department's page counts what it spends, the same rule as its total everywhere else
    // (CountsTowardDepartmentTotal); revenue it collects appears on its fund's page instead.
    private static BookDepartmentDto Department(DepartmentDetailDto d)
    {
        List<DetailLineDto> spending = [.. d.Lines.Where(l => l.AccountType.CountsTowardDepartmentTotal())];
        return new BookDepartmentDto(
            d.DepartmentCode, d.DepartmentName, string.IsNullOrWhiteSpace(d.Narrative) ? null : d.Narrative.Trim(),
            [.. spending.Select(l => $"{l.FundCode} {l.FundName}").Distinct().Order()],
            [.. spending.GroupBy(l => l.Category).OrderBy(g => g.Key)
                .Select(g => new BookAmountDto(Labels.Category(g.Key), g.Sum(l => l.PriorYearActual), g.Sum(l => l.CurrentYearBudget), g.Sum(l => l.Amount)))]);
    }

    private static List<BookLineGroupDto> LineItems(IReadOnlyList<BudgetLineDto> lines) =>
        [.. lines.OrderBy(l => l.FundCode).ThenBy(l => l.DepartmentCode is not null).ThenBy(l => l.DepartmentCode).ThenBy(l => l.AccountCode)
            .GroupBy(l => (l.FundCode, l.FundName, l.DepartmentCode, l.DepartmentName))
            .Select(g => new BookLineGroupDto(
                $"{g.Key.FundCode} {g.Key.FundName} · {(g.Key.DepartmentCode is null ? "Fund-level" : $"{g.Key.DepartmentCode} {g.Key.DepartmentName}")}",
                [.. g.Select(l => new BookLineDto(l.AccountNumber, l.AccountName, l.PriorYearActual, l.CurrentYearBudget, l.Amount))]))];

    private static BookAmountDto Row(string label, IEnumerable<BudgetLineDto> lines) =>
        new(label, lines.Sum(l => l.PriorYearActual), lines.Sum(l => l.CurrentYearBudget), lines.Sum(l => l.Amount));
}
