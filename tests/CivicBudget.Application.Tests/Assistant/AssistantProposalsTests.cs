using CivicBudget.Application.Assistant;
using CivicBudget.Application.Common;

namespace CivicBudget.Application.Tests.Assistant;

/// <summary>A proposal runs once, and not after half an hour, when the budget it was worked out from has moved on.</summary>
public class AssistantProposalsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AssistantProposalDto Add(AssistantProposals proposals, int rows = 1) =>
        proposals.Add("Raise utilities 5%", [.. Enumerable.Range(1, rows).Select(i => new ProposalRow($"Line {i}", "$100.00", "$105.00"))], null, "Change", null,
            _ => Task.FromResult(Result.Success(new ProposalOutcome("Done.", null))), "Raise utilities 5%", "BudgetVersion", Guid.NewGuid());

    [Fact]
    public void A_proposal_is_taken_once()
    {
        var proposals = new AssistantProposals(new Clock());
        AssistantProposalDto proposal = Add(proposals);

        Assert.NotNull(proposals.Take(proposal.Id));
        Assert.Null(proposals.Take(proposal.Id));
    }

    [Fact]
    public void A_proposal_expires_after_half_an_hour()
    {
        var clock = new Clock();
        var proposals = new AssistantProposals(clock);
        AssistantProposalDto fresh = Add(proposals);
        AssistantProposalDto stale = Add(proposals);

        clock.Now += AssistantProposals.Lifetime - TimeSpan.FromSeconds(1);
        Assert.NotNull(proposals.Take(fresh.Id));
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Null(proposals.Take(stale.Id));
    }

    [Fact]
    public void A_long_preview_shows_the_first_rows_and_counts_the_rest()
    {
        AssistantProposalDto proposal = Add(new AssistantProposals(new Clock()), rows: AssistantProposals.PreviewRows + 7);

        Assert.Equal(AssistantProposals.PreviewRows, proposal.Rows.Count);
        Assert.Equal(7, proposal.MoreRows);
    }
}
