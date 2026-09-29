using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

/// <summary>What the budget book page shows: the version, its message, and the choices it starts with.</summary>
public sealed record BudgetBookPageDto(
    Guid VersionId,
    int FiscalYear,
    string VersionLabel,
    BudgetStatus Status,
    string? MessageHeading,
    string? MessageBody,
    string? MessageSignedBy,
    string? MessageSignerTitle,
    bool CanEditMessage,
    BudgetBookOptions Defaults,
    bool CanSaveDefaults,
    /// <summary>Departments with lines and no narrative: their pages will print figures alone.</summary>
    IReadOnlyList<string> DepartmentsWithoutNarrative)
{
    public bool IsDraft => Status != BudgetStatus.Adopted;
}

public sealed record SaveBudgetMessageRequest(Guid VersionId, string? Heading, string? Body, string? SignedBy, string? SignerTitle);

public sealed record BudgetBookFile(string FileName, byte[] Content);

/// <summary>Draws a <see cref="BudgetBookDto"/> as a PDF. Implemented in Infrastructure with MigraDoc.</summary>
public interface IBudgetBookRenderer
{
    byte[] Render(BudgetBookDto book);
}

/// <summary>
/// The printable budget book: the whole government's budget as one PDF for council, the public, and
/// the file. It is assembled from the same reports the screens show (fund summary, categories,
/// department detail, certificate, plan, personnel), so the book and the screens always agree.
/// It covers every fund, so department users get none (like the other whole-government reports);
/// the budget message and the book's default sections belong to the fiscal authority.
/// </summary>
public interface IBudgetBookService
{
    Task<BudgetBookPageDto?> GetAsync(Guid versionId, CancellationToken ct = default);

    Task<Result> SaveMessageAsync(SaveBudgetMessageRequest request, CancellationToken ct = default);

    /// <summary>The sections a published book carries, and a printed one starts with.</summary>
    Task<Result> SaveDefaultsAsync(BudgetBookOptions options, CancellationToken ct = default);

    Task<BudgetBookOptions> GetDefaultsAsync(CancellationToken ct = default);

    Task<BudgetBookFile?> RenderAsync(Guid versionId, BudgetBookOptions options, CancellationToken ct = default);
}

public sealed class BudgetBookService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IReportService reports,
    ICertificateService certificates,
    IBudgetPlanService plans,
    IPersonnelReportService personnel,
    IBudgetEntryService entry,
    IFundService funds,
    IGovernmentLogoService logos,
    IBudgetBookRenderer renderer,
    TimeProvider clock) : IBudgetBookService
{
    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can change the budget book.";

    // A department user sees only their own department, and a book of part of the budget would read as the whole.
    private bool CanView => currentUser.GovernmentId is not null && !currentUser.IsDepartmentUser();

    public async Task<BudgetBookPageDto?> GetAsync(Guid versionId, CancellationToken ct = default)
    {
        if (!CanView)
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await db.BudgetVersions.AsNoTracking()
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.DepartmentRequests)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return null;
        }

        int year = await db.FiscalYears.Where(f => f.Id == version.FiscalYearId).Select(f => f.Year).SingleAsync(ct);
        List<string> silent = [.. version.Lines
            .Where(l => l.Department is not null && l.Account.Type.CountsTowardDepartmentTotal())
            .Select(l => l.Department!)
            .DistinctBy(d => d.Id)
            .Where(d => string.IsNullOrWhiteSpace(version.GetDepartmentRequest(d.Id)?.Narrative))
            .OrderBy(d => d.Code)
            .Select(d => $"{d.Code} {d.Name}")];

        return new BudgetBookPageDto(version.Id, year, version.Label, version.Status,
            version.MessageHeading, version.MessageBody, version.MessageSignedBy, version.MessageSignerTitle,
            version.IsEditable && currentUser.IsFiscalAuthority(),
            BudgetBookOptions.From(await db.BudgetBookSettings.AsNoTracking().FirstOrDefaultAsync(ct)),
            currentUser.IsFiscalAuthority(),
            silent);
    }

    public async Task<Result> SaveMessageAsync(SaveBudgetMessageRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await db.BudgetVersions.FirstOrDefaultAsync(v => v.Id == request.VersionId, ct);
        if (version is null)
        {
            return Result.Failure("Budget version was not found.");
        }

        try
        {
            version.SetMessage(request.Heading, request.Body, request.SignedBy, request.SignerTitle);
        }
        catch (DomainException ex)
        {
            return Result.Failure(nameof(request.Body), ex.Message);
        }

        return await db.TrySaveAsync(ct) ?? Result.Success();
    }

    public async Task<Result> SaveDefaultsAsync(BudgetBookOptions options, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority() || currentUser.GovernmentId is not { } governmentId)
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetBookSettings? settings = await db.BudgetBookSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new BudgetBookSettings(governmentId);
            db.BudgetBookSettings.Add(settings);
        }

        settings.Set(options.Outlook, options.Personnel, options.LineItems, options.Glossary);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<BudgetBookOptions> GetDefaultsAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return BudgetBookOptions.From(await db.BudgetBookSettings.AsNoTracking().FirstOrDefaultAsync(ct));
    }

    public async Task<BudgetBookFile?> RenderAsync(Guid versionId, BudgetBookOptions options, CancellationToken ct = default)
    {
        if (!CanView)
        {
            return null;
        }

        BudgetBookSources? sources = await SourcesAsync(versionId, ct);
        if (sources is null)
        {
            return null;
        }

        BudgetBookDto book = BudgetBookBuilder.Build(sources, options);
        string version = sources.Cover.VersionLabel.ToLowerInvariant().Replace(' ', '-');
        return new BudgetBookFile(
            $"budget-book-fy{sources.Cover.FiscalYear}-{version}{(sources.Cover.IsDraft ? "-proposed" : "")}.pdf",
            renderer.Render(book));
    }

    private async Task<BudgetBookSources?> SourcesAsync(Guid versionId, CancellationToken ct)
    {
        BudgetVersion? version;
        Government government;
        int fiscalYear;
        await using (ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct))
        {
            version = await db.BudgetVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version is null)
            {
                return null;
            }

            government = await db.Governments.AsNoTracking().SingleAsync(g => g.Id == version.GovernmentId, ct);
            fiscalYear = await db.FiscalYears.Where(f => f.Id == version.FiscalYearId).Select(f => f.Year).SingleAsync(ct);
        }

        // Each report checks the caller itself and returns null when it may not be shown; the book
        // needs the three that make its core, and leaves out an optional one that is not available.
        FundSummaryReportDto? summary = await reports.FundSummaryAsync(versionId, ct);
        CategoryReportDto? categories = await reports.RevenueVsExpenditureAsync(versionId, ct);
        DepartmentDetailReportDto? departments = await reports.DepartmentDetailAsync(versionId, null, ct);
        BudgetWorkspaceDto? workspace = await entry.GetWorkspaceAsync(versionId, ct);
        if (summary is null || categories is null || departments is null || workspace is null)
        {
            return null;
        }

        // The logo prints on the cover when it is a format a PDF can hold; a WebP logo leaves the name alone on the cover.
        Portal.PortalLogoDto? logo = await logos.GetAsync(ct);
        byte[]? cover = logo is { ContentType: "image/png" or "image/jpeg" } ? logo.Data : null;

        return new BudgetBookSources(
            new BookCoverDto(government.Name, fiscalYear, version.Label, version.Status, version.ResolutionNumber,
                version.AdoptedOnUtc is { } adopted ? OhioTime.DateOf(adopted) : null, version.AmendmentReason, cover,
                currentUser.DisplayName ?? "CivicBudget", clock.GetUtcNow()),
            version.MessageHeading, version.MessageBody, version.MessageSignedBy, version.MessageSignerTitle,
            summary, categories, departments, workspace.Lines,
            (await funds.ListAsync(includeInactive: true, ct)).ToDictionary(f => f.Code, f => f.Description),
            await certificates.GetAsync(versionId, ct),
            await plans.GetAsync(versionId, ct),
            await personnel.CostByFundAsync(versionId, ct));
    }
}
