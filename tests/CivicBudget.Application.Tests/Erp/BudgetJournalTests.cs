using CivicBudget.Application.Erp;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;
using CivicBudget.Infrastructure.Erp;

namespace CivicBudget.Application.Tests.Erp;

/// <summary>
/// What a budget journal posts (the budget less what the ERP already took), the file VIP imports,
/// and the simulated VIP that answers the send button in the demo.
/// </summary>
public class BudgetJournalTests
{
    private static readonly LineKey Overtime = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly LineKey Dispatch = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly LineKey Taxes = new(Guid.NewGuid(), null, Guid.NewGuid());

    private static JournalSourceLine Line(LineKey key, string number, decimal amount) => new(key, number, "Name " + number, amount);

    [Fact]
    public void The_first_send_of_a_year_is_the_whole_budget_without_its_empty_lines()
    {
        IReadOnlyList<JournalChange> changes = BudgetJournalBuilder.Changes(
            [Line(Overtime, "1000-110-5120", 38_000m), Line(Dispatch, "1000-110-5310", 62_000m), Line(Taxes, "1000-4110", 0m)], []);

        Assert.Equal([("1000-110-5120", 38_000m), ("1000-110-5310", 62_000m)], changes.Select(c => (c.AccountNumber, c.Change)));
    }

    [Fact]
    public void An_amendment_sends_only_what_moved_and_a_removed_line_sends_its_amount_back_out()
    {
        IReadOnlyList<JournalChange> changes = BudgetJournalBuilder.Changes(
            [Line(Overtime, "1000-110-5120", 53_000m), Line(Dispatch, "1000-110-5310", 62_000m)],
            [
                new SentAmount(Overtime, "1000-110-5120", 38_000m),
                new SentAmount(Dispatch, "1000-110-5310", 60_000m),
                new SentAmount(Dispatch, "1000-110-5310", 2_000m),      // two earlier journals add up
                new SentAmount(Taxes, "1000-4110", 410_000m),           // no longer in the budget
            ]);

        Assert.Equal([("1000-110-5120", 38_000m, 53_000m, 15_000m), ("1000-4110", 410_000m, 0m, -410_000m)],
            changes.Select(c => (c.AccountNumber, c.InErp, c.InBudget, c.Change)));
    }

    [Fact]
    public void A_budget_the_erp_already_matches_sends_nothing()
    {
        Assert.Empty(BudgetJournalBuilder.Changes([Line(Overtime, "1000-110-5120", 38_000m)], [new SentAmount(Overtime, "1000-110-5120", 38_000m)]));
    }

    [Fact]
    public void The_import_file_has_vips_four_columns_with_the_description_and_date_on_every_line()
    {
        var journal = new ErpBudgetJournal(Guid.NewGuid(), 2026, "FY2026 Amendment 1, resolution 2026-11", new DateOnly(2026, 6, 15),
            [new("1000-110-5120", 15_000m), new("1000-4110", -2_500.5m)]);

        byte[] csv = CsvWriter.ToCsv(BudgetJournalFile.ToTable(journal));

        string text = System.Text.Encoding.UTF8.GetString(csv).TrimStart('﻿');
        Assert.Equal(
            "Account,Amount,Description,Date\r\n" +
            "1000-110-5120,15000.00,\"FY2026 Amendment 1, resolution 2026-11\",2026-06-15\r\n" +
            "1000-4110,-2500.50,\"FY2026 Amendment 1, resolution 2026-11\",2026-06-15\r\n",
            text);
    }

    // ---- the simulated VIP ----------------------------------------------------------------------

    private static readonly ErpEntity MapleRidge = new(Guid.NewGuid(), "maple-ridge-oh", "Village of Maple Ridge", 1, AccountNumberFormat.UanVillage);

    private static ErpBudgetJournal Journal(params (string Account, decimal Amount)[] lines) =>
        new(Guid.NewGuid(), 2026, "Test", new DateOnly(2026, 6, 15), lines.Select(l => new ErpBudgetJournalLine(l.Account, l.Amount)).ToList());

    [Fact]
    public async Task Vip_posts_a_journal_on_accounts_it_knows_and_answers_a_retry_with_the_same_journal()
    {
        var vip = new SimulatedVipBudgetApi();
        ErpBudgetJournal journal = Journal(("1000-110-5120", 15_000m), ("1000-4110", -500m));

        ErpJournalAnswer first = await vip.PostBudgetJournalAsync(MapleRidge, journal);
        ErpJournalAnswer retry = await vip.PostBudgetJournalAsync(MapleRidge, journal);
        ErpJournalAnswer another = await vip.PostBudgetJournalAsync(MapleRidge, Journal(("1000-110-5120", 1m)));

        Assert.True(first.Posted);
        Assert.StartsWith("BJ2026-", first.JournalNumber, StringComparison.Ordinal);
        Assert.Equal(first.JournalNumber, retry.JournalNumber);               // recognized, not posted twice
        Assert.NotEqual(first.JournalNumber, another.JournalNumber);
    }

    [Fact]
    public async Task Vip_refuses_the_whole_journal_when_any_account_is_not_set_up()
    {
        ErpJournalAnswer answer = await new SimulatedVipBudgetApi().PostBudgetJournalAsync(MapleRidge,
            Journal(("1000-110-5120", 15_000m), ("1000-410-5420", 900m), ("1000-0410-5420", 1m)));

        Assert.False(answer.Posted);
        Assert.Null(answer.JournalNumber);
        Assert.Equal(["1000-0410-5420", "1000-410-5420"], answer.RefusedAccounts.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("posted nothing", answer.Message, StringComparison.Ordinal);
    }
}
