using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Erp;

/// <summary>How a budget journal reached the ERP.</summary>
public enum TransmissionMethod
{
    /// <summary>Posted straight to the ERP's API.</summary>
    Api = 1,

    /// <summary>Downloaded as an import file for someone to load into the ERP.</summary>
    File = 2,
}

/// <summary>Where one send stands. Only <see cref="Accepted"/> and <see cref="Imported"/> count as "in the ERP".</summary>
public enum TransmissionStatus
{
    /// <summary>Saved, and the ERP has been called; no answer recorded yet.</summary>
    Sending = 1,

    /// <summary>The ERP posted the journal.</summary>
    Accepted = 2,

    /// <summary>The ERP refused the journal; nothing was posted.</summary>
    Rejected = 3,

    /// <summary>The call failed without an answer, so whether it posted is unknown. Sending again is safe.</summary>
    Failed = 4,

    /// <summary>The import file was produced; someone still has to load it into the ERP.</summary>
    AwaitingImport = 5,

    /// <summary>Someone confirmed the file was loaded into the ERP.</summary>
    Imported = 6,

    /// <summary>Abandoned: a file never loaded, or a failed send the ERP shows it never posted.</summary>
    Discarded = 7,
}

/// <summary>
/// One budget journal sent (or on its way) to the ERP: the lines, the description and posting date
/// every line carries, and what became of it. A journal holds changes, not totals: an original budget
/// sends every amount, and an amendment sends only what moved since the last journal the ERP took.
/// Its id doubles as the ERP's idempotency key, so a send retried after a lost answer is recognized
/// instead of posted twice.
/// </summary>
public sealed class BudgetTransmission : Entity, ITenantOwned
{
    public const int DescriptionMaxLength = 100;
    public const int NameMaxLength = 200;
    public const int ReferenceMaxLength = 50;
    public const int MessageMaxLength = 1000;

    private readonly List<BudgetTransmissionLine> _lines = [];

    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }

    /// <summary>The label year of the version's fiscal year: journals are compared within a year.</summary>
    public int FiscalYear { get; private set; }

    public TransmissionMethod Method { get; private set; }
    public TransmissionStatus Status { get; private set; }

    /// <summary>The ERP's name as the connection or file reports it ("VIP (simulated)").</summary>
    public string TargetName { get; private set; }

    public string Description { get; private set; }
    public DateOnly PostingDate { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string UserId { get; private set; }
    public string UserName { get; private set; }

    /// <summary>When the ERP answered, or the file was confirmed or discarded.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>The ERP's own journal number, once it has posted one.</summary>
    public string? ErpReference { get; private set; }

    /// <summary>Why a send failed or was refused, in the ERP's words where it gave any.</summary>
    public string? Message { get; private set; }

    public IReadOnlyCollection<BudgetTransmissionLine> Lines => _lines.AsReadOnly();

    /// <summary>The ERP holds these amounts: the next journal is measured from them.</summary>
    public bool CountsAsSent => Status is TransmissionStatus.Accepted or TransmissionStatus.Imported;

    /// <summary>Unfinished: no other journal for the year may start until this one is settled.</summary>
    public bool IsOpen => Status is TransmissionStatus.Sending or TransmissionStatus.Failed or TransmissionStatus.AwaitingImport;

    public decimal NetChange => _lines.Sum(l => l.Amount);

    public BudgetTransmission(Guid governmentId, Guid budgetVersionId, int fiscalYear, TransmissionMethod method, string targetName,
        string description, DateOnly postingDate, string userId, string userName, DateTimeOffset nowUtc)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        FiscalYear = fiscalYear;
        Method = method;
        Status = method == TransmissionMethod.Api ? TransmissionStatus.Sending : TransmissionStatus.AwaitingImport;
        TargetName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(targetName, nameof(targetName)), NameMaxLength, nameof(targetName));
        Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(description, nameof(description)).Trim(), DescriptionMaxLength, "Description");
        PostingDate = postingDate;
        UserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        UserName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(userName, nameof(userName)), NameMaxLength, nameof(userName));
        CreatedAtUtc = nowUtc;
    }

    private BudgetTransmission()
    {
        TargetName = null!;
        Description = null!;
        UserId = null!;
        UserName = null!;
    }

    public BudgetTransmissionLine AddLine(Guid fundId, Guid? departmentId, Guid accountId, string accountNumber, decimal amount)
    {
        Guard.Against(Status is not (TransmissionStatus.Sending or TransmissionStatus.AwaitingImport), "Lines are added before a journal is sent.");
        Guard.Against(amount == 0m, "A journal line moves money; a zero line has nothing to post.");
        Guard.Against(_lines.Any(l => l.FundId == fundId && l.DepartmentId == departmentId && l.AccountId == accountId), $"{accountNumber} is already in this journal.");
        var line = new BudgetTransmissionLine(GovernmentId, Id, fundId, departmentId, accountId, accountNumber, amount);
        _lines.Add(line);
        return line;
    }

    /// <summary>The ERP posted it (or, on a retry, says it already had).</summary>
    public void MarkAccepted(string erpReference, DateTimeOffset nowUtc)
    {
        Guard.Against(Status is not (TransmissionStatus.Sending or TransmissionStatus.Failed), "Only a journal on its way to the ERP can be accepted.");
        Status = TransmissionStatus.Accepted;
        ErpReference = Guard.MaxLength(Guard.NotNullOrWhiteSpace(erpReference, nameof(erpReference)), ReferenceMaxLength, nameof(erpReference));
        Message = null;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>The ERP refused the journal as a whole; the refused accounts are marked on their lines.</summary>
    public void MarkRejected(IReadOnlyDictionary<string, string> reasonsByAccountNumber, string message, DateTimeOffset nowUtc)
    {
        Guard.Against(Status is not (TransmissionStatus.Sending or TransmissionStatus.Failed), "Only a journal on its way to the ERP can be rejected.");
        foreach (BudgetTransmissionLine line in _lines)
        {
            if (reasonsByAccountNumber.TryGetValue(line.AccountNumber, out string? reason))
            {
                line.Refuse(reason);
            }
        }

        Status = TransmissionStatus.Rejected;
        Message = Trim(message);
        CompletedAtUtc = nowUtc;
    }

    /// <summary>No answer came back. The journal may or may not have posted, so it stays open for a retry.</summary>
    public void MarkFailed(string message)
    {
        Guard.Against(Status is not (TransmissionStatus.Sending or TransmissionStatus.Failed), "Only a journal on its way to the ERP can fail.");
        Status = TransmissionStatus.Failed;
        Message = Trim(message);
    }

    /// <summary>Someone loaded the downloaded file into the ERP.</summary>
    public void ConfirmImported(string? erpReference, DateTimeOffset nowUtc)
    {
        Guard.Against(Status != TransmissionStatus.AwaitingImport, "Only a downloaded file waiting to be imported can be confirmed.");
        Status = TransmissionStatus.Imported;
        ErpReference = string.IsNullOrWhiteSpace(erpReference) ? null : Guard.MaxLength(erpReference.Trim(), ReferenceMaxLength, "Journal number");
        CompletedAtUtc = nowUtc;
    }

    /// <summary>A file that will not be imported, or a failed send the ERP shows it never posted.</summary>
    public void Discard(DateTimeOffset nowUtc)
    {
        Guard.Against(Status is not (TransmissionStatus.AwaitingImport or TransmissionStatus.Failed), "Only a file waiting to be imported or a failed send can be discarded.");
        Status = TransmissionStatus.Discarded;
        CompletedAtUtc = nowUtc;
    }

    private static string? Trim(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : message.Length <= MessageMaxLength ? message : message[..(MessageMaxLength - 1)] + "…";
}

/// <summary>One account's change in a budget journal: positive raises the ERP's budget, negative lowers it.</summary>
public sealed class BudgetTransmissionLine : Entity, ITenantOwned
{
    public const int AccountNumberMaxLength = 40;
    public const int ReasonMaxLength = 300;

    public Guid GovernmentId { get; private set; }
    public Guid BudgetTransmissionId { get; private set; }
    public Guid FundId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>The full number exactly as it was sent, so the history reads the way the ERP saw it.</summary>
    public string AccountNumber { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Why the ERP refused this account, when it did.</summary>
    public string? RefusedReason { get; private set; }

    internal BudgetTransmissionLine(Guid governmentId, Guid transmissionId, Guid fundId, Guid? departmentId, Guid accountId, string accountNumber, decimal amount)
    {
        Guard.Against(!Money.IsStorable(amount), Money.TooLargeMessage);
        GovernmentId = governmentId;
        BudgetTransmissionId = transmissionId;
        FundId = fundId;
        DepartmentId = departmentId;
        AccountId = accountId;
        AccountNumber = Guard.MaxLength(Guard.NotNullOrWhiteSpace(accountNumber, nameof(accountNumber)), AccountNumberMaxLength, nameof(accountNumber));
        Amount = Money.Round(amount);
    }

    private BudgetTransmissionLine()
    {
        AccountNumber = null!;
    }

    internal void Refuse(string reason) =>
        RefusedReason = reason.Length <= ReasonMaxLength ? reason : reason[..(ReasonMaxLength - 1)] + "…";
}
