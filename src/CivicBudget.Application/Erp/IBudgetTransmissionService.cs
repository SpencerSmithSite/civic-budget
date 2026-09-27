using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Erp;

namespace CivicBudget.Application.Erp;

/// <summary>One account in a sent journal, with the ERP's reason when it refused it.</summary>
public sealed record TransmissionLineDto(string AccountNumber, decimal Amount, string? RefusedReason);

/// <summary>One send, for the history and for the open-send banner.</summary>
public sealed record TransmissionDto(
    Guid Id,
    string VersionLabel,
    TransmissionMethod Method,
    TransmissionStatus Status,
    string TargetName,
    string Description,
    DateOnly PostingDate,
    DateTimeOffset CreatedAtUtc,
    string UserName,
    DateTimeOffset? CompletedAtUtc,
    string? ErpReference,
    string? Message,
    IReadOnlyList<TransmissionLineDto> Lines)
{
    public decimal Increases => Lines.Where(l => l.Amount > 0m).Sum(l => l.Amount);
    public decimal Decreases => Lines.Where(l => l.Amount < 0m).Sum(l => l.Amount);
    public decimal Net => Lines.Sum(l => l.Amount);
    public bool IsOpen => Status is TransmissionStatus.Sending or TransmissionStatus.Failed or TransmissionStatus.AwaitingImport;
}

/// <summary>Everything the send page shows for one budget version.</summary>
public sealed record SendPageDto(
    Guid VersionId,
    int FiscalYear,
    string VersionLabel,
    DateOnly FiscalYearStart,
    DateOnly FiscalYearEnd,
    /// <summary>The API connection's name, or null when only the import file is available.</summary>
    string? ApiName,
    /// <summary>Why this version cannot be sent, or null when it can.</summary>
    string? CannotSendReason,
    string DefaultDescription,
    DateOnly DefaultPostingDate,
    IReadOnlyList<JournalChange> Changes,
    /// <summary>An unfinished send for this fiscal year; nothing new may start until it is settled.</summary>
    TransmissionDto? Open,
    /// <summary>Every send for this fiscal year, newest first.</summary>
    IReadOnlyList<TransmissionDto> History)
{
    public bool CanSend => CannotSendReason is null && Open is null && Changes.Count > 0;
}

/// <summary>The description and posting date typed on the send page; every journal line carries both.</summary>
public sealed record SendBudgetRequest(Guid VersionId, string Description, DateOnly PostingDate);

/// <summary>
/// Sends an adopted budget to the ERP as a budget journal of changes: by API where a connection is
/// set up, or as an import file someone loads and then confirms. Keeps every send, and never lets
/// two unfinished sends for one year exist at once, so the ERP's budget is never posted twice.
/// Administrators and the Fiscal Officer.
/// </summary>
public interface IBudgetTransmissionService
{
    Task<SendPageDto?> GetAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>Posts the journal to the ERP's API.</summary>
    Task<Result<TransmissionDto>> SendAsync(SendBudgetRequest request, CancellationToken ct = default);

    /// <summary>Records the journal as an import file waiting to be loaded; download it with <see cref="FileAsync"/>.</summary>
    Task<Result<TransmissionDto>> CreateFileAsync(SendBudgetRequest request, CancellationToken ct = default);

    /// <summary>The import file for a file send, or null when there is no such send.</summary>
    Task<(string FileName, ExportTable Table)?> FileAsync(Guid transmissionId, CancellationToken ct = default);

    /// <summary>Sends a failed journal again, unchanged and under the same id, so the ERP can tell if it already has it.</summary>
    Task<Result<TransmissionDto>> RetryAsync(Guid transmissionId, CancellationToken ct = default);

    Task<Result> ConfirmImportedAsync(Guid transmissionId, string? erpReference, CancellationToken ct = default);

    Task<Result> DiscardAsync(Guid transmissionId, CancellationToken ct = default);
}
