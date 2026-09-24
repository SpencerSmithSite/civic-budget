using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;

namespace CivicBudget.Domain.Tests.Budgets;

/// <summary>
/// Each department hands its request to the fiscal officer. The version owns the rules:
/// submit only while Draft and only with lines, return only what was submitted, narratives travel
/// into amendments but statuses do not.
/// </summary>
public class DepartmentRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    private static (BudgetVersion Version, Department Police, Department Streets) DraftWithPoliceLine()
    {
        BudgetVersion version = TestData.DraftVersion();
        Department police = TestData.Police();
        Department streets = TestData.Streets();
        version.AddLine(TestData.GeneralFund(), police, TestData.Salaries(), 500_000m, 470_000m, 480_000m);
        return (version, police, streets);
    }

    [Fact]
    public void A_department_without_a_request_is_in_progress_and_not_submitted()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();

        Assert.Null(version.GetDepartmentRequest(police.Id));
        Assert.False(version.IsDepartmentSubmitted(police.Id));
        Assert.Empty(version.DepartmentRequests);
    }

    [Fact]
    public void Writing_a_narrative_starts_the_request_and_trims_it()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();

        version.SetDepartmentNarrative(police, "  Replace one cruiser.  ");

        DepartmentRequest request = Assert.Single(version.DepartmentRequests);
        Assert.Equal(police.Id, request.DepartmentId);
        Assert.Equal(DepartmentRequestStatus.InProgress, request.Status);
        Assert.Equal("Replace one cruiser.", request.Narrative);

        version.SetDepartmentNarrative(police, "   ");
        Assert.Null(request.Narrative);
        Assert.Throws<DomainException>(() => version.SetDepartmentNarrative(police, new string('x', DepartmentRequest.NarrativeMaxLength + 1)));
    }

    [Fact]
    public void Submit_records_who_and_when_and_locks_the_department()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();

        version.SubmitDepartment(police, "user-chief", "Chief Hale", Now);

        DepartmentRequest request = version.GetDepartmentRequest(police.Id)!;
        Assert.Equal(DepartmentRequestStatus.Submitted, request.Status);
        Assert.True(version.IsDepartmentSubmitted(police.Id));
        Assert.Equal("Chief Hale", request.SubmittedByUserName);
        Assert.Equal(Now, request.SubmittedAtUtc);
        Assert.Throws<DomainException>(() => version.SubmitDepartment(police, "user-chief", "Chief Hale", Now)); // not twice
    }

    [Fact]
    public void Submit_needs_a_draft_version_and_at_least_one_line()
    {
        (BudgetVersion version, Department police, Department streets) = DraftWithPoliceLine();

        Assert.Throws<DomainException>(() => version.SubmitDepartment(streets, "u", "Sam", Now)); // nothing budgeted for Streets

        version.Propose();
        Assert.Throws<DomainException>(() => version.SubmitDepartment(police, "u", "Chief Hale", Now)); // the officer owns it now
    }

    [Fact]
    public void Return_needs_a_note_and_reopens_the_department()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();
        Assert.Throws<DomainException>(() => version.ReturnDepartment(police, "Trim overtime", Now)); // nothing submitted yet

        version.SubmitDepartment(police, "user-chief", "Chief Hale", Now);
        Assert.Throws<DomainException>(() => version.ReturnDepartment(police, " ", Now.AddDays(1)));

        version.ReturnDepartment(police, "Trim overtime to last year's level.", Now.AddDays(1));

        DepartmentRequest request = version.GetDepartmentRequest(police.Id)!;
        Assert.Equal(DepartmentRequestStatus.Returned, request.Status);
        Assert.False(version.IsDepartmentSubmitted(police.Id));
        Assert.Equal("Trim overtime to last year's level.", request.ReturnNote);
        Assert.Equal(Now.AddDays(1), request.ReturnedAtUtc);

        // Submitting again clears the note: the department answered it.
        version.SubmitDepartment(police, "user-chief", "Chief Hale", Now.AddDays(2));
        Assert.Null(request.ReturnNote);
        Assert.Equal(Now.AddDays(2), request.SubmittedAtUtc);
    }

    [Fact]
    public void Departments_of_another_government_are_refused()
    {
        (BudgetVersion version, _, _) = DraftWithPoliceLine();
        Department foreign = TestData.Police(TestData.OtherGovernmentId);

        Assert.Throws<DomainException>(() => version.SetDepartmentNarrative(foreign, "x"));
        Assert.Throws<DomainException>(() => version.SubmitDepartment(foreign, "u", "n", Now));
    }

    [Fact]
    public void An_amendment_keeps_narratives_but_starts_a_new_round()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();
        version.SetDepartmentNarrative(police, "Replace one cruiser.");
        version.SubmitDepartment(police, "user-chief", "Chief Hale", Now);
        version.Propose();
        version.Adopt("2026-40", "user-fd", Now.AddMonths(2));

        BudgetVersion amendment = version.CreateAmendment("Dispatch contract increase");

        DepartmentRequest copied = Assert.Single(amendment.DepartmentRequests);
        Assert.Equal(amendment.Id, copied.BudgetVersionId);
        Assert.Equal("Replace one cruiser.", copied.Narrative);
        Assert.Equal(DepartmentRequestStatus.InProgress, copied.Status);
        Assert.Null(copied.SubmittedAtUtc);
        Assert.Equal(DepartmentRequestStatus.Submitted, version.GetDepartmentRequest(police.Id)!.Status); // the adopted one is untouched
    }

    [Fact]
    public void Adopted_versions_refuse_narrative_changes()
    {
        (BudgetVersion version, Department police, _) = DraftWithPoliceLine();
        version.Propose();
        version.Adopt("2026-40", "user-fd", Now);

        Assert.Throws<DomainException>(() => version.SetDepartmentNarrative(police, "Too late"));
    }
}
