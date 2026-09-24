using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets;

/// <summary>Where a department's part of a budget version stands with the fiscal officer.</summary>
public enum DepartmentRequestStatus
{
    /// <summary>The department is still entering figures. Also the state of a department with no row at all.</summary>
    InProgress = 1,

    /// <summary>The department has handed its request to the fiscal officer; department users can no longer edit it.</summary>
    Submitted = 2,

    /// <summary>The fiscal officer sent it back with a note; the department may edit and submit again.</summary>
    Returned = 3,
}

/// <summary>
/// One department's budget request within a version: its narrative (the "budget message" a fire chief
/// writes to explain the year's request) and whether it has been submitted to the fiscal officer.
/// In Ohio practice each department hands its request to the fiscal officer, who assembles the
/// whole budget; this row records that hand-off per department so the officer can see who is in.
/// Created and changed only through <see cref="BudgetVersion"/>, which owns the editability rule.
/// Submit and return are recorded as named events on the version, so their bookkeeping fields are
/// <see cref="NotAuditedAttribute">not audited</see> field by field; Status, Narrative, and ReturnNote still are.
/// </summary>
[Audited]
public sealed class DepartmentRequest : Entity, ITenantOwned
{
    public const int NarrativeMaxLength = 4000;
    public const int ReturnNoteMaxLength = 1000;

    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public Guid DepartmentId { get; private set; }

    public DepartmentRequestStatus Status { get; private set; }

    /// <summary>The department's own explanation of its request. Published with the budget.</summary>
    public string? Narrative { get; private set; }

    [NotAudited]
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    [NotAudited]
    public string? SubmittedByUserId { get; private set; }

    /// <summary>Kept as text so the workspace can say "submitted by Chief Hale" without a join to Identity.</summary>
    [NotAudited]
    public string? SubmittedByUserName { get; private set; }

    /// <summary>The fiscal officer's reason for sending the request back. Cleared on the next submit.</summary>
    public string? ReturnNote { get; private set; }

    [NotAudited]
    public DateTimeOffset? ReturnedAtUtc { get; private set; }

    public bool IsSubmitted => Status == DepartmentRequestStatus.Submitted;

    internal DepartmentRequest(Guid governmentId, Guid budgetVersionId, Guid departmentId)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        DepartmentId = departmentId;
        Status = DepartmentRequestStatus.InProgress;
    }

    private DepartmentRequest()
    {
    }

    internal void SetNarrative(string? narrative) =>
        Narrative = string.IsNullOrWhiteSpace(narrative)
            ? null
            : Guard.MaxLength(narrative.Trim(), NarrativeMaxLength, nameof(narrative));

    internal void Submit(string userId, string userName, DateTimeOffset nowUtc)
    {
        Guard.Against(IsSubmitted, "This department's request has already been submitted.");
        // Validate before mutating, as Return does: a bad argument must not leave a half-submitted request.
        string byId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        string byName = Guard.NotNullOrWhiteSpace(userName, nameof(userName));
        SubmittedByUserId = byId;
        SubmittedByUserName = byName;
        Status = DepartmentRequestStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        ReturnNote = null;
        ReturnedAtUtc = null;
    }

    internal void Return(string note, DateTimeOffset nowUtc)
    {
        Guard.Against(!IsSubmitted, "Only a submitted request can be returned to the department.");
        // Validate before mutating so a bad note leaves the request submitted rather than half returned.
        string trimmedNote = Guard.MaxLength(Guard.NotNullOrWhiteSpace(note, nameof(note)), ReturnNoteMaxLength, nameof(note));
        Status = DepartmentRequestStatus.Returned;
        ReturnNote = trimmedNote;
        ReturnedAtUtc = nowUtc;
    }

    /// <summary>
    /// An amendment keeps the department's narrative (it still describes the year) but starts a new
    /// round of submissions, so status and the submit/return trail are not copied.
    /// </summary>
    internal DepartmentRequest CopyTo(Guid targetVersionId) =>
        new(GovernmentId, targetVersionId, DepartmentId) { Narrative = Narrative };
}
