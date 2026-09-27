# Walkthrough 22: Send the budget to VIP

Phase 22 brought the ERP's books into CivicBudget. Phase 23 sends the other way: once council
adopts the budget, one button puts it into VIP, where purchase orders are checked against it.
Until now someone retyped every appropriation. The button is simple to draw and easy to get wrong,
so most of this walkthrough is about what can go wrong and how the design rules it out.

## 1. What VIP expects

VIP takes a budget journal by API or by import file, whichever the customer uses. Every line
carries four fields: `Account` (the full account number), `Amount`, `Description` (one for the
whole journal, repeated on every line), and `Date` (the journal's posting date, also repeated).
`ErpBudgetJournal` is that contract, `IErpBudgetApi` posts one, and `BudgetJournalFile` writes one
as the four-column file.

## 2. Three ways a send button fails

1. **The same change posted twice.** The obvious case is pressing the button twice. The subtle
   one is a send whose answer never came back: it may have posted, and a naive retry posts again.
2. **A change counted that never arrived.** If a downloaded file counts as "sent", and nobody
   imports it, every later journal is measured from numbers VIP does not have.
3. **A journal half-posted.** If VIP takes the accounts it knows and skips one, the two systems
   disagree on one line, and nothing on either screen says so.

The rest of this walkthrough is how each one is closed.

## 3. A journal of changes

A budget journal adds to what the ERP already holds. So a journal is not "the budget"; it is the
budget less what earlier journals for that fiscal year already posted. `BudgetJournalBuilder` does
exactly that, as a pure function:

| Situation | What the journal posts |
|---|---|
| First send of the year | Every line's amount |
| An amendment | Only the accounts it changed, by the difference |
| A line removed from the budget | Its amount, as a decrease |
| The ERP already matches | Nothing, so the page says "The ERP already has this budget" |

The last row is what makes pressing Send twice harmless: the second press has nothing to send.
In the demo, the FY2025 and FY2026 originals were sent in January, so the FY2026 amendment's page
opens on exactly the supplemental appropriation: police overtime and the dispatch contract,
+$21,000.00.

VIP adds a journal's amounts to an account's budget, which is how a journal works in every
fund-accounting system I have used, so a decrease goes as a negative amount.

## 4. Every send is a record, saved first

`BudgetTransmission` holds the journal's lines, description, posting date, how it went (API or
file), and what became of it:

| Status | Meaning | Counts as in the ERP | Holds the year |
|---|---|---|---|
| Sending | Saved; the ERP is being called | No | Yes |
| Accepted | The ERP posted it (journal number kept) | Yes | No |
| Rejected | The ERP refused it; nothing posted | No | No |
| Failed | No answer; it may or may not have posted | No | Yes |
| AwaitingImport | The file was downloaded | No | Yes |
| Imported | Someone confirmed the file was loaded | Yes | No |
| Discarded | Given up (a file never loaded, or a failed send VIP never posted) | No | No |

The record is saved **before** the ERP is called. If the process died mid-call, the send would
still be on record as unfinished instead of forgotten.

## 5. Retrying without posting twice

The record's id travels with the journal as `ExternalId`. When a call throws (a timeout, a dropped
connection), the send is marked Failed, not Rejected, because nobody knows whether VIP posted it.
The page says exactly that and offers **Try again**, which sends the same journal under the same
id. VIP answers a journal id it has seen with the answer it gave the first time, so the retry
comes back Accepted with the original journal number and nothing posts twice.

The simulated ERP keeps a dictionary of ids it has answered to behave this way. The integration
test `A_lost_answer_is_retried_under_the_same_id_and_vip_posts_it_once` wraps it in an API that
posts the journal and then throws, which is precisely the case that breaks a naive retry.

## 6. Files count only when confirmed

Downloading the import file creates a send in AwaitingImport. It does not count as in VIP until
someone presses **Confirm imported** (optionally typing VIP's journal number), because a file on
someone's desktop has changed nothing in VIP. Until it is confirmed or discarded, the year is held:
no other send can start, so the same changes cannot reach VIP by a second route.

## 7. One unfinished send per year, twice over

The service refuses a new send while an earlier one for the year is Sending, Failed, or
AwaitingImport, and says why. But two people clicking at the same moment could both pass that
check. A filtered unique index settles it in the database:

```csharp
builder.HasIndex(t => new { t.GovernmentId, t.FiscalYear })
    .IsUnique()
    .HasFilter("[Status] IN (1, 4, 5)");   // Sending, Failed, AwaitingImport
```

The second save fails, and `TrySaveAsync` turns that into "someone else started a send at the
same moment". The same pattern keeps fund-level budget lines unique (`BudgetLineConfiguration`).

## 8. Whole or nothing

When VIP refuses any account, it posts nothing and says which accounts and why. The send becomes
Rejected, the refused lines are marked in its details, nothing counts as sent, and the year is free
to send again once the account exists in VIP. The simulated ERP refuses any account that is not in
its own chart, so the demo can show it: start an amendment, add fuel for Building & Zoning (an
account the ERP has never had), adopt, send.

## 9. The page

Budget versions, the latest adopted version, **Send to ERP**. The page leads with anything
unfinished (a failed send with Try again, or a file waiting to be confirmed), then the journal's
description and posting date (defaults: "FY2026 Amendment 1, resolution 2026-11", and the adoption
date for an amendment or the year's first day for an original), four totals, the lines with what
the ERP has now, what the budget says, and the difference, and the year's history with each journal
number. Nothing is sent until the confirm dialog, which repeats the description, date, line count,
and net change.

## 10. Tests

- **Domain** (`BudgetTransmissionTests`): which states count and which hold the year; a failed
  send can still be accepted on retry; a file must be confirmed; zero and duplicate lines refused.
- **Unit** (`BudgetJournalTests`): first send, amendment, removed line, already matching; the
  import file's exact bytes; the simulated ERP's retry and whole-journal refusal.
- **Integration** (`BudgetTransmissionServiceTests`): the seeded amendment sends its two changes and
  then has nothing to send; only the latest adopted version, and only in its year; a file holds the
  year until confirmed; a refused account posts nothing; a lost answer retried once; the database
  refusing a second unfinished send; Department Users and Viewers refused.
- **bUnit**: the page sends only after confirming, with the typed description and date; a failed
  send disables new sends and retries; the workflow bar offers Send only on the latest adopted version.

## 11. What I confirmed about VIP

I built this before seeing VIP's interface, so I wrote down four assumptions and confirmed each
(2026-09-27):

- A journal's amounts add to an account's budget, so a decrease is a negative amount.
- The import file is CSV with a header row, `yyyy-MM-dd` dates, and amounts without separators.
- The description holds up to 100 characters.
- The API answers a repeated journal id with its first answer.

Each lives in one place in the code: `BudgetJournalBuilder`, `BudgetJournalFile`,
`BudgetTransmission.DescriptionMaxLength`, and the real `IErpBudgetApi` adapter.

## 12. The app says "ERP"

VIP is the ERP I know, but CivicBudget is meant to be taken on by any ERP vendor. So nothing the
app shows names a product: the button is "Send to ERP", the connection in the demo is "ERP
(simulated)", and messages say "the ERP". The docs keep VIP as the example, because that is the
system these rules were checked against.
