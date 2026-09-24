using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>The bar renders exactly the actions the service says are available, and dialogs drive the service.</summary>
public class WorkflowBarTests : BunitContext
{
    private static readonly BudgetVersionSummaryDto Version = new(Guid.CreateVersion7(), 2027, 1, "Original", BudgetStatus.Draft, null, null, 95);

    private static WorkflowStateDto State(BudgetStatus status, bool fd, bool overLimitBlock = false) => new(
        status,
        CanPropose: fd && status == BudgetStatus.Draft,
        CanReturnToDraft: fd && status == BudgetStatus.Proposed,
        CanAdopt: fd && status == BudgetStatus.Proposed,
        CanAmend: fd && status == BudgetStatus.Adopted,
        HasOpenSibling: false,
        overLimitBlock ? [new AppropriationLimitResult(Guid.CreateVersion7(), AppropriationLimitSeverity.Error, 100m)] : []);

    private FakeWorkflow workflow = null!;

    private IRenderedComponent<WorkflowBar> RenderBar(WorkflowStateDto state, bool asFinanceDirector = true)
    {
        workflow = new FakeWorkflow();
        Services.AddSingleton<IBudgetWorkflowService>(workflow);
        Services.AddSingleton<IPublishingService>(new FakePublishing());
        Services.AddSingleton<ToastService>();
        // Dialogs hand focus back to the button that opened them when they close.
        JSInterop.SetupVoid("civicBudget.rememberFocus");
        JSInterop.SetupVoid("civicBudget.restoreFocus");
        var auth = AddAuthorization();
        auth.SetAuthorized("dana");
        if (asFinanceDirector)
        {
            auth.SetRoles(Roles.FinanceDirector);
            auth.SetPolicies(Policies.CanPublish);
        }

        return Render<WorkflowBar>(p => p
            .Add(x => x.Version, Version with { Status = state.Status })
            .Add(x => x.State, state));
    }

    [Fact]
    public void Draft_offers_propose_and_blocks_it_when_over_limit()
    {
        IRenderedComponent<WorkflowBar> bar = RenderBar(State(BudgetStatus.Draft, fd: true, overLimitBlock: true));

        var propose = bar.Find("button:contains('Propose to council')");
        Assert.True(propose.HasAttribute("disabled"));
        Assert.Contains("cannot propose or adopt", bar.Markup);
        Assert.Empty(bar.FindAll("button:contains('Adopt')"));
    }

    [Fact]
    public void Viewer_gets_no_workflow_buttons()
    {
        IRenderedComponent<WorkflowBar> bar = RenderBar(State(BudgetStatus.Draft, fd: false), asFinanceDirector: false);

        Assert.Empty(bar.FindAll("button:not(.btn-close)"));
    }

    [Fact]
    public async Task Adopt_dialog_requires_a_resolution_number_and_calls_the_service()
    {
        IRenderedComponent<WorkflowBar> bar = RenderBar(State(BudgetStatus.Proposed, fd: true));
        await bar.Find("button:contains('Adopt')").ClickAsync(new());

        var confirm = bar.Find(".modal-footer button.btn-success");
        Assert.True(confirm.HasAttribute("disabled"));

        await bar.Find("#resolution").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "2026-44" });
        Assert.False(bar.Find(".modal-footer button.btn-success").HasAttribute("disabled"));

        await bar.Find(".modal-footer button.btn-success").ClickAsync(new());
        Assert.Equal("2026-44", workflow.AdoptedWith);
    }

    [Fact]
    public void Adopted_and_published_offers_unpublish_and_amendment()
    {
        var snapshot = new SnapshotSummaryDto(Guid.CreateVersion7(), Version.Id, 2027, "Original", Domain.Publishing.SnapshotStatus.Active, DateTimeOffset.UtcNow, "Dana", null, 95);
        workflow = new FakeWorkflow();
        Services.AddSingleton<IBudgetWorkflowService>(workflow);
        Services.AddSingleton<IPublishingService>(new FakePublishing());
        Services.AddSingleton<ToastService>();
        var auth = AddAuthorization();
        auth.SetAuthorized("dana");
        auth.SetPolicies(Policies.CanPublish);

        IRenderedComponent<WorkflowBar> bar = Render<WorkflowBar>(p => p
            .Add(x => x.Version, Version with { Status = BudgetStatus.Adopted })
            .Add(x => x.State, State(BudgetStatus.Adopted, fd: true))
            .Add(x => x.ActiveSnapshot, snapshot));

        Assert.Single(bar.FindAll("button:contains('Unpublish')"));
        Assert.Single(bar.FindAll("button:contains('Start an amendment')"));
        Assert.Empty(bar.FindAll("button:contains('Publish to portal')"));
    }

    private sealed class FakeWorkflow : IBudgetWorkflowService
    {
        public string? AdoptedWith { get; private set; }
        public Task<WorkflowStateDto?> GetStateAsync(Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> ProposeAsync(Guid versionId, bool acknowledgeWarnings, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ReturnToDraftAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> AdoptAsync(Guid versionId, string resolutionNumber, bool acknowledgeWarnings, CancellationToken ct = default)
        {
            AdoptedWith = resolutionNumber;
            return Task.FromResult(Result.Success());
        }
        public Task<Result<Guid>> CreateAmendmentAsync(Guid adoptedVersionId, string reason, CancellationToken ct = default) => Task.FromResult(Result.Success(Guid.CreateVersion7()));

        public Task<Result<StartBudgetResultDto>> StartBudgetAsync(StartBudgetRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePublishing : IPublishingService
    {
        public Task<IReadOnlyList<SnapshotSummaryDto>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SnapshotSummaryDto>>([]);
        public Task<Result<Guid>> PublishAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Result.Success(Guid.CreateVersion7()));
        public Task<Result> UnpublishAsync(Guid snapshotId, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }
}
