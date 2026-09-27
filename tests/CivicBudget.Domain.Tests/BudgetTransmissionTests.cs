using CivicBudget.Domain.Common;
using CivicBudget.Domain.Erp;

namespace CivicBudget.Domain.Tests;

/// <summary>
/// A send to the ERP moves through a few states, and only two of them mean "the ERP has it". These
/// rules are what stop a budget change from being posted twice or counted when it never arrived.
/// </summary>
public class BudgetTransmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);

    private static BudgetTransmission Send(TransmissionMethod method = TransmissionMethod.Api)
    {
        var t = new BudgetTransmission(Guid.NewGuid(), Guid.NewGuid(), 2026, method, "ERP (simulated)", "  FY2026 Amendment 1  ", new DateOnly(2026, 6, 15), "u1", "Dana", Now);
        t.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1000-110-5120", 15_000m);
        t.AddLine(Guid.NewGuid(), null, Guid.NewGuid(), "1000-4110", -2_500m);
        return t;
    }

    [Fact]
    public void An_api_send_starts_open_and_counts_only_once_the_erp_posts_it()
    {
        BudgetTransmission t = Send();
        Assert.Equal((TransmissionStatus.Sending, true, false), (t.Status, t.IsOpen, t.CountsAsSent));
        Assert.Equal("FY2026 Amendment 1", t.Description);
        Assert.Equal(12_500m, t.NetChange);

        t.MarkAccepted("BJ2026-00301", Now);

        Assert.Equal((TransmissionStatus.Accepted, false, true, "BJ2026-00301"), (t.Status, t.IsOpen, t.CountsAsSent, t.ErpReference));
        Assert.Throws<DomainException>(() => t.MarkAccepted("BJ2026-00302", Now));                 // one answer per send
        Assert.Throws<DomainException>(() => t.AddLine(Guid.NewGuid(), null, Guid.NewGuid(), "1000-4130", 1m));
    }

    [Fact]
    public void A_send_with_no_answer_stays_open_and_can_still_be_accepted_on_a_retry()
    {
        BudgetTransmission t = Send();

        t.MarkFailed("No answer from ERP (simulated): timed out");
        Assert.Equal((TransmissionStatus.Failed, true, false), (t.Status, t.IsOpen, t.CountsAsSent));

        t.MarkAccepted("BJ2026-00301", Now);
        Assert.True(t.CountsAsSent);
        Assert.Null(t.Message);
    }

    [Fact]
    public void A_refused_journal_marks_the_accounts_and_counts_for_nothing()
    {
        BudgetTransmission t = Send();

        t.MarkRejected(new Dictionary<string, string> { ["1000-4110"] = "Account is not set up in the ERP." }, "The ERP posted nothing.", Now);

        Assert.Equal((TransmissionStatus.Rejected, false, false), (t.Status, t.IsOpen, t.CountsAsSent));
        Assert.Equal([null, "Account is not set up in the ERP."], t.Lines.Select(l => l.RefusedReason));
    }

    [Fact]
    public void A_file_waits_for_someone_to_confirm_it_was_imported()
    {
        BudgetTransmission t = Send(TransmissionMethod.File);
        Assert.Equal((TransmissionStatus.AwaitingImport, true, false), (t.Status, t.IsOpen, t.CountsAsSent));
        Assert.Throws<DomainException>(() => t.MarkAccepted("x", Now));                            // a file is not answered by an API

        t.ConfirmImported(" BJ2026-00410 ", Now);

        Assert.Equal((TransmissionStatus.Imported, true, "BJ2026-00410"), (t.Status, t.CountsAsSent, t.ErpReference));
        Assert.Throws<DomainException>(() => t.Discard(Now));                                    // too late: it is in the ERP
    }

    [Fact]
    public void Only_an_unfinished_file_or_failed_send_can_be_discarded()
    {
        BudgetTransmission file = Send(TransmissionMethod.File);
        file.Discard(Now);
        Assert.Equal((TransmissionStatus.Discarded, false, false), (file.Status, file.IsOpen, file.CountsAsSent));

        BudgetTransmission accepted = Send();
        accepted.MarkAccepted("BJ1", Now);
        Assert.Throws<DomainException>(() => accepted.Discard(Now));
    }

    [Fact]
    public void A_journal_line_moves_money_once()
    {
        BudgetTransmission t = Send();
        Guid fund = Guid.NewGuid(), account = Guid.NewGuid();

        Assert.Throws<DomainException>(() => t.AddLine(fund, null, account, "1000-4130", 0m));
        t.AddLine(fund, null, account, "1000-4130", 10m);
        Assert.Throws<DomainException>(() => t.AddLine(fund, null, account, "1000-4130", 5m));
        Assert.Throws<DomainException>(() => new BudgetTransmission(Guid.NewGuid(), Guid.NewGuid(), 2026, TransmissionMethod.Api, "ERP", new string('x', 101), new DateOnly(2026, 1, 1), "u", "n", Now));
    }
}
