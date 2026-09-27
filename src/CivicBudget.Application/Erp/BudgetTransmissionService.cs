using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Erp;

public sealed class BudgetTransmissionService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IEnumerable<IErpBudgetApi> apis,
    TimeProvider clock) : IBudgetTransmissionService
{
    /// <summary>At most one connection is registered; with none, only the import file is offered.</summary>
    private readonly IErpBudgetApi? api = apis.FirstOrDefault();

    public string ErpName => api?.SystemName ?? "the ERP";

    public async Task<SendPageDto?> GetAsync(Guid versionId, CancellationToken ct = default)
    {
        // The page lists every account in the budget, so it is for the people who may send it.
        if (!currentUser.IsFiscalAuthority())
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await LoadAsync(db, versionId, ct) is not { } loaded)
        {
            return null;
        }

        (BudgetVersion version, FiscalYear year, Government government) = loaded;
        IReadOnlyList<JournalChange> changes = await ChangesAsync(db, version, year, government, ct);
        List<TransmissionDto> history = await HistoryAsync(db, year.Year, ct);

        return new SendPageDto(
            version.Id, year.Year, version.Label, year.StartDate, year.EndDate, ErpName, api?.Name,
            CannotSend(version),
            DefaultDescription(version, year),
            DefaultPostingDate(version, year),
            changes,
            history.FirstOrDefault(h => h.IsOpen),
            history);
    }

    public Task<Result<TransmissionDto>> SendAsync(SendBudgetRequest request, CancellationToken ct = default) =>
        StartAsync(request, TransmissionMethod.Api, ct);

    public Task<Result<TransmissionDto>> CreateFileAsync(SendBudgetRequest request, CancellationToken ct = default) =>
        StartAsync(request, TransmissionMethod.File, ct);

    public async Task<(string FileName, ExportTable Table)?> FileAsync(Guid transmissionId, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetTransmission? t = await db.BudgetTransmissions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == transmissionId && x.Method == TransmissionMethod.File, ct);
        if (t is null)
        {
            return null;
        }

        string label = await db.BudgetVersions.Where(v => v.Id == t.BudgetVersionId).Select(v => v.VersionNumber == 1 ? "original" : "amendment-" + (v.VersionNumber - 1)).SingleAsync(ct);
        return ($"budget-journal-fy{t.FiscalYear}-{label}.csv", BudgetJournalFile.ToTable(JournalOf(t)));
    }

    public async Task<Result<TransmissionDto>> RetryAsync(Guid transmissionId, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<TransmissionDto>(NotAllowed);
        }

        if (api is null)
        {
            return Result.Failure<TransmissionDto>(NoApi);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetTransmission? t = await db.BudgetTransmissions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == transmissionId, ct);
        if (t is null || t.Method != TransmissionMethod.Api || t.Status is not (TransmissionStatus.Failed or TransmissionStatus.Sending))
        {
            return Result.Failure<TransmissionDto>("Only a send that got no answer can be tried again.");
        }

        Government government = await db.Governments.SingleAsync(g => g.Id == t.GovernmentId, ct);
        return await PostAsync(db, t, government, ct);
    }

    public async Task<Result> ConfirmImportedAsync(Guid transmissionId, string? erpReference, CancellationToken ct = default) =>
        await SettleAsync(transmissionId, t =>
        {
            t.ConfirmImported(erpReference, clock.GetUtcNow());
            return $"Confirmed the FY{t.FiscalYear} budget journal file was imported into {t.TargetName}{(t.ErpReference is null ? "" : $" as {t.ErpReference}")}";
        }, ct);

    public async Task<Result> DiscardAsync(Guid transmissionId, CancellationToken ct = default) =>
        await SettleAsync(transmissionId, t =>
        {
            string what = t.Method == TransmissionMethod.File ? "budget journal file" : "failed send";
            t.Discard(clock.GetUtcNow());
            return $"Discarded the FY{t.FiscalYear} {what} for {t.TargetName}";
        }, ct);

    // ---- sending ------------------------------------------------------------------------------

    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can send the budget to the ERP.";
    private const string NoApi = "No ERP connection is set up for this government. Download the import file instead.";

    private async Task<Result<TransmissionDto>> StartAsync(SendBudgetRequest request, TransmissionMethod method, CancellationToken ct)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<TransmissionDto>(NotAllowed);
        }

        if (method == TransmissionMethod.Api && api is null)
        {
            return Result.Failure<TransmissionDto>(NoApi);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await LoadAsync(db, request.VersionId, ct) is not { } loaded)
        {
            return Result.Failure<TransmissionDto>("Budget version was not found.");
        }

        (BudgetVersion version, FiscalYear year, Government government) = loaded;
        if (CannotSend(version) is { } reason)
        {
            return Result.Failure<TransmissionDto>(reason);
        }

        string description = request.Description?.Trim() ?? "";
        if (description.Length == 0)
        {
            return Result.Failure<TransmissionDto>(nameof(request.Description), "Give the journal a description; the ERP puts it on every line.");
        }

        if (description.Length > BudgetTransmission.DescriptionMaxLength)
        {
            return Result.Failure<TransmissionDto>(nameof(request.Description), $"Keep the description to {BudgetTransmission.DescriptionMaxLength} characters.");
        }

        if (request.PostingDate < year.StartDate || request.PostingDate > year.EndDate)
        {
            return Result.Failure<TransmissionDto>(nameof(request.PostingDate),
                $"The posting date must fall in {year.Label} ({Date(year.StartDate)} to {Date(year.EndDate)}).");
        }

        if (await db.BudgetTransmissions.AnyAsync(t => t.FiscalYear == year.Year
                && (t.Status == TransmissionStatus.Sending || t.Status == TransmissionStatus.Failed || t.Status == TransmissionStatus.AwaitingImport), ct))
        {
            return Result.Failure<TransmissionDto>($"An earlier {year.Label} send is not finished. Settle it first (try again, confirm the import, or discard it), so nothing posts twice.");
        }

        IReadOnlyList<JournalChange> changes = await ChangesAsync(db, version, year, government, ct);
        if (changes.Count == 0)
        {
            return Result.Failure<TransmissionDto>($"{ErpName} already has this budget. There is nothing to send.");
        }

        string target = method == TransmissionMethod.Api ? api!.Name : ErpName == "the ERP" ? "ERP import file" : $"{ErpName} import file";
        var transmission = new BudgetTransmission(government.Id, version.Id, year.Year, method, target, description, request.PostingDate,
            currentUser.UserId!, currentUser.DisplayName ?? currentUser.UserId!, clock.GetUtcNow());
        foreach (JournalChange c in changes)
        {
            transmission.AddLine(c.Key.FundId, c.Key.DepartmentId, c.Key.AccountId, c.AccountNumber, c.Change);
        }

        db.BudgetTransmissions.Add(transmission);
        if (method == TransmissionMethod.File)
        {
            Audit(db, transmission, $"Created the {year.Label} {version.Label} budget journal file for {ErpName}: {Summary(transmission)}");
        }

        // Saved before the ERP is called: if the process dies mid-call, the send is on record as
        // unfinished and can be retried under the same id instead of being forgotten.
        if (await db.TrySaveAsync(ct) is not null)
        {
            return Result.Failure<TransmissionDto>($"Someone else started a {year.Label} send at the same moment. Reload to see it.");
        }

        return method == TransmissionMethod.File
            ? Result.Success(ToDto(transmission, version.Label))
            : await PostAsync(db, transmission, government, ct);
    }

    private async Task<Result<TransmissionDto>> PostAsync(ICivicBudgetDbContext db, BudgetTransmission t, Government government, CancellationToken ct)
    {
        ErpJournalAnswer? answer;
        try
        {
            answer = await api!.PostBudgetJournalAsync(EntityFor(government), JournalOf(t), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // No answer means no knowledge: the journal may have posted. It stays open, and a retry
            // sends the same id, which the ERP recognizes.
            answer = null;
            t.MarkFailed($"No answer from {t.TargetName}: {ex.Message}");
        }

        string versionLabel = await db.BudgetVersions.Where(v => v.Id == t.BudgetVersionId)
            .Select(v => v.VersionNumber == 1 ? "Original" : "Amendment " + (v.VersionNumber - 1)).SingleAsync(ct);
        if (answer is { Posted: true })
        {
            t.MarkAccepted(answer.JournalNumber!, clock.GetUtcNow());
            Audit(db, t, $"Sent FY{t.FiscalYear} {versionLabel} to {t.TargetName} as journal {t.ErpReference}: {Summary(t)}");
        }
        else if (answer is not null)
        {
            t.MarkRejected(answer.RefusedAccounts, answer.Message ?? "Refused.", clock.GetUtcNow());
            Audit(db, t, $"{t.TargetName} refused the FY{t.FiscalYear} {versionLabel} journal; nothing was posted ({answer.RefusedAccounts.Count} account{(answer.RefusedAccounts.Count == 1 ? "" : "s")} refused)");
        }
        else
        {
            Audit(db, t, $"Sending FY{t.FiscalYear} {versionLabel} to {t.TargetName} got no answer; it can be tried again safely");
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(t, versionLabel));
    }

    private async Task<Result> SettleAsync(Guid transmissionId, Func<BudgetTransmission, string> settle, CancellationToken ct)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetTransmission? t = await db.BudgetTransmissions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == transmissionId, ct);
        if (t is null)
        {
            return Result.Failure("That send was not found.");
        }

        try
        {
            Audit(db, t, settle(t));
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- reading ------------------------------------------------------------------------------

    private static async Task<(BudgetVersion, FiscalYear, Government)?> LoadAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct)
    {
        BudgetVersion? version = await db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return null;
        }

        FiscalYear year = await db.FiscalYears.SingleAsync(fy => fy.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        return (version, year, government);
    }

    /// <summary>The version against what the ERP already took for the year: accepted sends and confirmed imports.</summary>
    private static async Task<IReadOnlyList<JournalChange>> ChangesAsync(ICivicBudgetDbContext db, BudgetVersion version, FiscalYear year, Government government, CancellationToken ct)
    {
        List<SentAmount> sent = await db.BudgetTransmissions
            .Where(t => t.FiscalYear == year.Year && (t.Status == TransmissionStatus.Accepted || t.Status == TransmissionStatus.Imported))
            .SelectMany(t => t.Lines)
            .Select(l => new SentAmount(new LineKey(l.FundId, l.DepartmentId, l.AccountId), l.AccountNumber, l.Amount))
            .ToListAsync(ct);

        IReadOnlyList<JournalChange> changes = BudgetJournalBuilder.Changes(
            version.Lines.Select(l => new JournalSourceLine(
                new LineKey(l.FundId, l.DepartmentId, l.AccountId),
                AccountNumber.Compose(government.AccountNumberFormat, l.Fund.Code, l.Department?.Code, l.Account.Code),
                l.Account.Name, l.Amount)),
            sent);

        // A line taken out of the budget still needs a name on the preview.
        if (changes.Any(c => c.AccountName.Length == 0))
        {
            List<Guid> ids = changes.Where(c => c.AccountName.Length == 0).Select(c => c.Key.AccountId).ToList();
            Dictionary<Guid, string> names = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
            changes = changes.Select(c => c.AccountName.Length > 0 ? c : c with { AccountName = names.GetValueOrDefault(c.Key.AccountId, "") }).ToList();
        }

        return changes;
    }

    private static async Task<List<TransmissionDto>> HistoryAsync(ICivicBudgetDbContext db, int fiscalYear, CancellationToken ct)
    {
        List<BudgetTransmission> sends = await db.BudgetTransmissions.Include(t => t.Lines)
            .Where(t => t.FiscalYear == fiscalYear).OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        List<Guid> versionIds = sends.Select(t => t.BudgetVersionId).Distinct().ToList();
        Dictionary<Guid, int> numbers = await db.BudgetVersions.Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.VersionNumber, ct);
        return sends.Select(t => ToDto(t, numbers[t.BudgetVersionId] == 1 ? "Original" : $"Amendment {numbers[t.BudgetVersionId] - 1}")).ToList();
    }

    // ---- rules and helpers --------------------------------------------------------------------

    /// <summary>The same rule as publishing: only the latest adopted version is the budget.</summary>
    private string? CannotSend(BudgetVersion version) => version switch
    {
        _ when !currentUser.IsFiscalAuthority() => NotAllowed,
        { Status: not BudgetStatus.Adopted } => $"Only an adopted budget can be sent to {ErpName}. Adopt it first.",
        { SupersededByVersionId: not null } => "A later amendment replaced this version. Send the latest adopted version instead.",
        _ => null,
    };

    private static string DefaultDescription(BudgetVersion version, FiscalYear year)
    {
        string text = version.ResolutionNumber is { } resolution ? $"{year.Label} {version.Label}, resolution {resolution}" : $"{year.Label} {version.Label}";
        return text.Length <= BudgetTransmission.DescriptionMaxLength ? text : text[..BudgetTransmission.DescriptionMaxLength];
    }

    /// <summary>An original budget takes effect on the first day of the year; an amendment on the day it was adopted.</summary>
    private static DateOnly DefaultPostingDate(BudgetVersion version, FiscalYear year)
    {
        if (!version.IsAmendment || version.AdoptedOnUtc is not { } adopted)
        {
            return year.StartDate;
        }

        var day = DateOnly.FromDateTime(InOhio(adopted).DateTime);
        return day < year.StartDate ? year.StartDate : day > year.EndDate ? year.EndDate : day;
    }

    /// <summary>The adoption's date where the council met (Ohio is all Eastern), or UTC if the host lacks zone data.</summary>
    private static DateTimeOffset InOhio(DateTimeOffset when)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(when, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return when.ToUniversalTime();
        }
    }

    private static ErpBudgetJournal JournalOf(BudgetTransmission t) =>
        new(t.Id, t.FiscalYear, t.Description, t.PostingDate, t.Lines.OrderBy(l => l.AccountNumber, StringComparer.Ordinal).Select(l => new ErpBudgetJournalLine(l.AccountNumber, l.Amount)).ToList());

    private static ErpEntity EntityFor(Government g) => new(g.Id, g.PublicSlug, g.Name, g.FiscalYearStartMonth, g.AccountNumberFormat);

    private void Audit(ICivicBudgetDbContext db, BudgetTransmission t, string description) =>
        db.AuditEntries.Add(AuditEntry.Event(t.GovernmentId, nameof(BudgetVersion), t.BudgetVersionId, description,
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));

    private static string Summary(BudgetTransmission t) =>
        $"{t.Lines.Count} line{(t.Lines.Count == 1 ? "" : "s")}, net {(t.NetChange < 0 ? "-" : "+")}{Math.Abs(t.NetChange).ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"))}";

    private static string Date(DateOnly d) => d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static TransmissionDto ToDto(BudgetTransmission t, string versionLabel) => new(
        t.Id, versionLabel, t.Method, t.Status, t.TargetName, t.Description, t.PostingDate, t.CreatedAtUtc, t.UserName, t.CompletedAtUtc,
        t.ErpReference, t.Message,
        t.Lines.OrderBy(l => l.AccountNumber, StringComparer.Ordinal).Select(l => new TransmissionLineDto(l.AccountNumber, l.Amount, l.RefusedReason)).ToList());
}
