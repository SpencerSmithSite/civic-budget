using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Export;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>A send service that serves a prepared page and records what the page asked it to do.</summary>
internal sealed class FakeTransmissionService : IBudgetTransmissionService
{
    public SendPageDto? Page { get; set; }
    public SendBudgetRequest? Sent { get; private set; }
    public Guid? Retried { get; private set; }
    public TransmissionDto? Answer { get; set; }

    public string ErpName => "VIP";

    public Task<SendPageDto?> GetAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Page);

    public Task<Result<TransmissionDto>> SendAsync(SendBudgetRequest request, CancellationToken ct = default)
    {
        Sent = request;
        return Task.FromResult(Answer is null ? Result.Failure<TransmissionDto>("not set up") : Result.Success(Answer));
    }

    public Task<Result<TransmissionDto>> RetryAsync(Guid transmissionId, CancellationToken ct = default)
    {
        Retried = transmissionId;
        return Task.FromResult(Answer is null ? Result.Failure<TransmissionDto>("not set up") : Result.Success(Answer));
    }

    public Task<Result<TransmissionDto>> CreateFileAsync(SendBudgetRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(string FileName, ExportTable Table)?> FileAsync(Guid transmissionId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result> ConfirmImportedAsync(Guid transmissionId, string? erpReference, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result> DiscardAsync(Guid transmissionId, CancellationToken ct = default) => throw new NotSupportedException();
}
