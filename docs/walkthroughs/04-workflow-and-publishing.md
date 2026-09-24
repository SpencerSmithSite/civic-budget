# Walkthrough 04: Workflow, amendments, and publishing

Phase 4 moved a budget from draft to adopted, added amendments, and published
adopted budgets to the snapshot tables the portal reads.

---

## 1. The workflow is three domain methods and one service

`BudgetVersion.Propose()`, `ReturnToDraft()`, `Adopt(resolution, user, now)` and
`CreateAmendment(reason)` have existed since Phase 1 with their own tests.
Phase 4 adds `Application/Budgets/BudgetWorkflowService`, which wraps each
transition with the four things the domain cannot know on its own:

1. **Who is asking.** The fiscal officer only (a role check then; since Phase 9c,
   `IsFiscalAuthority()`, which also admits the Administrator).
2. **The appropriation limit** in the government's mode (SPEC section 5.1).
   Block: refuse. Warn: refuse unless the caller acknowledged.
3. **The audit event.** `AuditEntry.Event(...)` with a sentence a person can
   read: "Adopted by resolution 2026-44". Field diffs alone cannot say why.
4. **Cross-aggregate consequences.** Adopting an amendment marks the
   previously adopted version superseded (`SupersedePriorAdoptedAsync`).

All of that is one private `TransitionAsync` helper, so Propose and Adopt
differ only in the domain method they call and the sentence they log.

`GetStateAsync` returns a `WorkflowStateDto`: the status, which buttons
this user may see, and the limit results. `BlocksTransition` and
`RequiresAcknowledgement` are derived on the DTO so the bar and the dialogs
share the same reading of the numbers.

---

## 2. Amendments are new versions, not edits

`CreateAmendmentAsync` loads the adopted version, enforces
`BudgetVersion.EnsureNoOpenVersion` against its siblings (one budget in
progress per fiscal year), and calls `adopted.CreateAmendment(reason)`, which
returns a new Draft with copies of every line and beginning balance.
`db.BudgetVersions.Add(amendment)` tracks the whole graph as Added because
the root is added explicitly (contrast with ADR-0018, where children were
discovered through an existing root).

The adopted original is never modified. When the amendment is adopted, the
original gets `SupersededByVersionId` and an audit event; the version list
still shows it, and the publishing history still shows its snapshot.

*Ohio framing:* an amendment is a supplemental appropriation ordinance. It
goes to council like the original, gets its own resolution number, and
replaces the appropriation measure. That is exactly the object model.

---

## 3. Publishing freezes; the portal reads only what was frozen

`Domain/Publishing/PublishedBudgetSnapshot.Capture(...)` copies an adopted
version into three tables:

| Table | What it holds |
|---|---|
| `PublishedBudgetSnapshots` | Header: government slug/name/description, fiscal year, version label, resolution, who published, when, status |
| `PublishedBudgetSnapshotLines` | Every line with fund, department, and account **codes and names copied**, plus amounts |
| `PublishedBudgetSnapshotFunds` | Every fund in the budget with its description and beginning balance |

Nothing in those tables is a foreign key to live data. Rename an account
next year and last year's published budget still says what it said. The
domain test `Snapshot_is_independent_of_later_changes_to_the_chart_of_accounts`
proves it.

`PublishingService.PublishAsync`:
- Fiscal officer only; the version must be Adopted (the domain refuses otherwise).
  Since Phase 18 it must also be the *latest* adopted version of its year.
- If the fiscal year already has an active snapshot, it is marked
  **Superseded** (kept, never deleted). Exactly one active snapshot per year.
- Audit events on both the snapshot and the version.
- Calls `IPublishedSnapshotCacheInvalidator`, a no-op until Phase 5 wires
  output caching. Publishing already knows it must invalidate; the cache
  just does not exist yet.

`UnpublishAsync` flips the status to **Unpublished** and records who and
when. The rows stay. Citizens see nothing for that year until something is
published again.

---

## 4. `PublicPortalDbContext`: the boundary as code (ADR-0006)

`Infrastructure/Persistence/PublicPortalDbContext.cs` is the portal's only
door to the database:

- It **maps three tables** (four once department narratives were published in
  Phase 9d, plus the government logo in Phase 12). `Model.GetEntityTypes()`
  returns exactly `PublishedBudgetSnapshots`, `PublishedBudgetSnapshotLines`,
  `PublishedBudgetSnapshotFunds`. There is no `BudgetLine`, no
  `AspNetUsers`, no `Governments` to query, join, or leak. A test asserts
  the list.
- A **global query filter** keeps only `Status == Active` snapshots, and
  lines/funds whose snapshot is active. Unpublished and superseded data is
  invisible to portal code even if it asks for it by id.
- **`SaveChanges` throws.** The context is read-only by construction, with
  `NoTracking` as the default so nothing is held in memory per request.
- It **owns no migrations**. The admin context creates the tables; the two
  contexts share one `PublishedSnapshotModel.Configure` so their mappings
  cannot drift. The admin context adds the foreign key to `Governments`
  separately, because the portal must not map `Governments`.

Interview framing: this is defense in depth expressed structurally. The
service already refuses to publish drafts; the snapshot already contains no
live references; and the portal's context physically cannot reach anything
else. Any one layer failing still leaves two.

`dotnet ef` now needs `--context CivicBudgetDbContext` because the project
has two contexts (CLAUDE.md has the command).

---

## 5. Dialogs without JavaScript

`Components/Common/ConfirmDialog.razor` renders Bootstrap's modal markup
from Blazor state: `Show()` sets a flag, the markup appears with
`modal show d-block` and a backdrop, and `OnConfirm` returns whether to
close. No Bootstrap JavaScript, so the dialogs can carry inputs (a resolution
number, an amendment reason, an acknowledgement checkbox) and disable their
confirm button until the input is valid. `window.confirm` from Phase 3 remained
only for "remove this line" until Phase 4.5 replaced it. (Phase 18 added a few
lines of script that keep Tab inside an open dialog and hand focus back to the
button that opened it.)

`WorkflowBar.razor` owns six dialogs and asks the services; the workspace
page just reloads on `OnChanged`. `AcknowledgeWarning.razor` is shared by
the Propose and Adopt dialogs.

---

## 6. Things deliberately not done yet

- Output caching and its invalidation (Phase 5; the hook exists).
- A SQL login for the portal with `SELECT` only on the three tables
  (Phase 7 hardening; the code boundary is in place).
- Publishing a version other than the latest adopted one was allowed at this
  point. In Phase 18 I decided against it: once an amendment is adopted the
  earlier version is history, and republishing it would put the pre-amendment
  numbers back in front of citizens (walkthrough 20).
