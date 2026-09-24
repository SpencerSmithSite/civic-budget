using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Budgets;

public sealed class BudgetWorkflowService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IBudgetWorkflowService
{
    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can move a budget through the workflow.";

    public async Task<WorkflowStateDto?> GetStateAsync(Guid versionId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadAsync(db, versionId, ct);
        if (version is null)
        {
            return null;
        }

        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        bool isFiscalAuthority = currentUser.IsFiscalAuthority();
        bool hasOpenSibling = await db.BudgetVersions.AnyAsync(v => v.FiscalYearId == version.FiscalYearId && v.Id != version.Id && v.Status != BudgetStatus.Adopted, ct);
        bool yearClosed = await db.FiscalYears.Where(f => f.Id == version.FiscalYearId).Select(f => f.IsClosed).SingleAsync(ct);

        return new WorkflowStateDto(
            version.Status,
            CanPropose: isFiscalAuthority && version.Status == BudgetStatus.Draft,
            CanReturnToDraft: isFiscalAuthority && version.Status == BudgetStatus.Proposed,
            CanAdopt: isFiscalAuthority && version.Status == BudgetStatus.Proposed,
            CanAmend: isFiscalAuthority && version.Status == BudgetStatus.Adopted && version.SupersededByVersionId is null && !hasOpenSibling && !yearClosed,
            hasOpenSibling,
            LimitResults(version, government),
            IsSuperseded: version.SupersededByVersionId is not null);
    }

    public Task<Result> ProposeAsync(Guid versionId, bool acknowledgeWarnings, CancellationToken ct = default) =>
        TransitionAsync(versionId, acknowledgeWarnings, v => v.Propose(), "Proposed to council", ct);

    public Task<Result> ReturnToDraftAsync(Guid versionId, CancellationToken ct = default) =>
        TransitionAsync(versionId, acknowledgeWarnings: true, v => v.ReturnToDraft(), "Returned to draft", ct, checkLimit: false);

    public Task<Result> AdoptAsync(Guid versionId, string resolutionNumber, bool acknowledgeWarnings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolutionNumber))
        {
            return Task.FromResult(Result.Failure(nameof(resolutionNumber), "Enter the ordinance or resolution number."));
        }

        return TransitionAsync(
            versionId,
            acknowledgeWarnings,
            v => v.Adopt(resolutionNumber, currentUser.UserId!, clock.GetUtcNow()),
            $"Adopted by resolution {resolutionNumber.Trim()}",
            ct,
            afterTransition: SupersedePriorAdoptedAsync);
    }

    public async Task<Result<Guid>> CreateAmendmentAsync(Guid adoptedVersionId, string reason, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<Guid>(NotAllowed);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<Guid>(nameof(reason), "Explain why the budget is being amended (this is recorded with the amendment).");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? adopted = await LoadAsync(db, adoptedVersionId, ct);
        if (adopted is null)
        {
            return Result.Failure<Guid>("Budget version was not found.");
        }

        List<BudgetVersion> siblings = await db.BudgetVersions.Where(v => v.FiscalYearId == adopted.FiscalYearId).ToListAsync(ct);
        FiscalYear fiscalYear = await db.FiscalYears.SingleAsync(f => f.Id == adopted.FiscalYearId, ct);
        BudgetVersion amendment;
        try
        {
            fiscalYear.EnsureOpenForNewVersions();
            BudgetVersion.EnsureNoOpenVersion(siblings);
            Guard.Against(adopted.SupersededByVersionId is not null, "This version has already been amended; amend the latest adopted version instead.");
            amendment = adopted.CreateAmendment(reason);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(ex.Message);
        }

        db.BudgetVersions.Add(amendment);
        db.AuditEntries.Add(Event(amendment, $"Amendment started from {adopted.Label}: {reason.Trim()}"));
        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Result.Failure<Guid>(conflict.Errors);
        }

        return Result.Success(amendment.Id);
    }

    public async Task<Result<StartBudgetResultDto>> StartBudgetAsync(StartBudgetRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<StartBudgetResultDto>(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        FiscalYear? year = await db.FiscalYears.FirstOrDefaultAsync(f => f.Id == request.FiscalYearId, ct);
        if (year is null)
        {
            return Result.Failure<StartBudgetResultDto>("Fiscal year was not found.");
        }

        if (await db.BudgetVersions.AnyAsync(v => v.FiscalYearId == year.Id, ct))
        {
            return Result.Failure<StartBudgetResultDto>($"{year.Label} already has a budget. Open it from Budget versions.");
        }

        BudgetVersion version;
        IReadOnlyList<string> skipped = [];
        string description;
        try
        {
            year.EnsureOpenForNewVersions();
            if (!request.StartFromPriorYear)
            {
                version = BudgetVersion.CreateOriginal(year.GovernmentId, year.Id);
                description = $"Started the {year.Label} budget with no lines";
            }
            else
            {
                BudgetVersion? prior = await LatestAdoptedAsync(db, year.Year - 1, ct);
                if (prior is null)
                {
                    return Result.Failure<StartBudgetResultDto>($"FY{year.Year - 1} has no adopted budget to start from. Start an empty budget instead.");
                }

                Dictionary<Guid, Domain.Funds.Fund> funds = await db.Funds.ToDictionaryAsync(f => f.Id, ct);
                var options = new BudgetSeedOptions(request.AdjustmentPercent, request.Scope, request.RoundToWholeDollars);
                (version, skipped) = BudgetVersion.CreateOriginalFrom(prior, year.Id, funds, options);
                description = $"Started the {year.Label} budget from FY{year.Year - 1} {prior.Label}{SeedNote(options)}";
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<StartBudgetResultDto>(ex.Message);
        }

        db.BudgetVersions.Add(version);
        db.AuditEntries.Add(Event(version, description));
        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Result.Failure<StartBudgetResultDto>(conflict.Errors);
        }

        return Result.Success(new StartBudgetResultDto(version.Id, version.Lines.Count, skipped));
    }

    /// <summary>The version of that year citizens would see: adopted, and not replaced by a later amendment.</summary>
    private static Task<BudgetVersion?> LatestAdoptedAsync(ICivicBudgetDbContext db, int fiscalYear, CancellationToken ct) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.BeginningBalances)
            .Where(v => v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null
                && db.FiscalYears.Any(f => f.Id == v.FiscalYearId && f.Year == fiscalYear))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

    private static string SeedNote(BudgetSeedOptions options)
    {
        if (options.AdjustmentPercent == 0m)
        {
            return options.RoundToWholeDollars ? ", amounts rounded to whole dollars" : ", amounts unchanged";
        }

        string which = options.Scope switch
        {
            SeedAdjustmentScope.AppropriationsOnly => "appropriations",
            SeedAdjustmentScope.RevenueEstimatesOnly => "revenue estimates",
            _ => "every line",
        };
        string change = (options.AdjustmentPercent > 0 ? "+" : "") + options.AdjustmentPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
        return $", {which} {change}{(options.RoundToWholeDollars ? ", rounded to whole dollars" : "")}";
    }

    // ---- shared transition plumbing --------------------------------------------------------------

    private async Task<Result> TransitionAsync(
        Guid versionId,
        bool acknowledgeWarnings,
        Action<BudgetVersion> transition,
        string auditDescription,
        CancellationToken ct,
        bool checkLimit = true,
        Func<ICivicBudgetDbContext, BudgetVersion, CancellationToken, Task>? afterTransition = null)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadAsync(db, versionId, ct);
        if (version is null)
        {
            return Result.Failure("Budget version was not found.");
        }

        if (checkLimit)
        {
            Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
            IReadOnlyList<AppropriationLimitResult> results = LimitResults(version, government);
            if (results.Any(r => r.BlocksWorkflow))
            {
                return Result.Failure("One or more funds appropriate more than their estimated resources. Reduce appropriations or raise estimated resources before continuing.");
            }

            if (!acknowledgeWarnings && results.Any(r => r.Severity == AppropriationLimitSeverity.Warning))
            {
                return Result.Failure(nameof(acknowledgeWarnings), "One or more funds are over their estimated resources. Acknowledge the warning to continue.");
            }
        }

        try
        {
            transition(version);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        db.AuditEntries.Add(Event(version, auditDescription));
        if (afterTransition is not null)
        {
            await afterTransition(db, version, ct);
        }

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        return Result.Success();
    }

    /// <summary>When an amendment is adopted, the previously adopted version of that year is marked superseded.</summary>
    private async Task SupersedePriorAdoptedAsync(ICivicBudgetDbContext db, BudgetVersion adopted, CancellationToken ct)
    {
        if (!adopted.IsAmendment)
        {
            return;
        }

        BudgetVersion? prior = await db.BudgetVersions
            .Where(v => v.FiscalYearId == adopted.FiscalYearId && v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null && v.Id != adopted.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (prior is not null)
        {
            prior.MarkSupersededBy(adopted);
            db.AuditEntries.Add(Event(prior, $"Superseded by {adopted.Label}"));
        }
    }

    private static IReadOnlyList<AppropriationLimitResult> LimitResults(BudgetVersion version, Government government) =>
        AppropriationLimitCheck.EvaluateAll(FundBalanceCalculator.CalculateAll(version), government.AppropriationLimitMode);

    private AuditEntry Event(BudgetVersion version, string description) =>
        AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id, description,
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow());

    private static Task<BudgetVersion?> LoadAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.BeginningBalances)
            .Include(v => v.DepartmentRequests)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
}
