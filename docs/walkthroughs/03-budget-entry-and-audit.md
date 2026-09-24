# Walkthrough 03: Budget entry, fund balances, and the audit trail

Phase 3 made the budget editable: two ways to enter it, live fund balances, and a
field-by-field history of every change.

---

## 1. One workspace, two views, zero rules in components

`Application/Budgets/BudgetEntryService.GetWorkspaceAsync` returns
everything the screen needs in one object (`BudgetWorkspaceDto`): the
version header, the lines the *current user may see*, each line's
`CanEdit` already decided, per-fund balances with the appropriation limit
already evaluated, and the lookups for adding lines.

The two entry modes are two components over the same DTO:

- `DepartmentEntryView.razor` walks the tree built by
  `Application/Budgets/BudgetGrouping.ByDepartment` (department → fund →
  category, with subtotals at every level).
- `AccountLineGrid.razor` is a QuickGrid over the flat list with filters.

Neither component knows a role name or a status. `line.CanEdit` says
whether to render an input; `workspace.CanAddLines` says whether to show
the add form. The rule lives once, in `BudgetLinePermissions` (Phase 2),
and the service applies it.

*Why reload the whole workspace after every edit?* Simplicity and
correctness: the fund balance panel and subtotals are always computed from
saved data, never from screen state. A village budget has ~100 lines; the
reload is one query. If a county with 5,000 lines showed up, the service
could return a delta instead, and nothing in the components would change.

---

## 2. Department users: filtered lines, whole-fund balances

Two deliberate asymmetries in `GetWorkspaceAsync`:

- **Lines** are filtered to the user's departments (from the
  `department_id` claims). A department user never sees, and cannot edit,
  another department's numbers. `BudgetEntryServiceTests.Department_head_cannot_edit_another_departments_line_even_by_id`
  proves that guessing a line id doesn't help.
- **Fund balances** are computed from *every* line in the version. A fund
  total that excluded other departments would make the appropriation check
  meaningless. So the Streets director sees that SCM&R is $35,908 over its
  limit even though half of that fund's lines are not theirs to edit.

---

## 3. The fund balance panel is arithmetic the domain already owns

`FundBalancePanel.razor` prints `FundBalanceSummary` and
`AppropriationLimitResult` values from Phase 1's `FundBalanceCalculator`
and `AppropriationLimitCheck`. The government's `AppropriationLimitMode`
decides whether an over-appropriated fund is red (Block) or yellow (Warn).
Beginning balances are editable inline for the Administrator and Fiscal Officer only
(`BudgetLinePermissions.CanEditBeginningBalances`).

Enforcement of the limit at workflow transitions is Phase 4. Phase 3 makes
it *visible* while staff work, which is what a budget officer actually
needs in October.

---

## 4. The audit trail: an interceptor, an attribute, and an append-only table

`Infrastructure/Persistence/Interceptors/AuditInterceptor.cs`

```
SaveChangesAsync
  → AuditInterceptor.SavingChangesAsync
      walks ChangeTracker.Entries()
      for each entity marked [Audited] that is Added / Modified / Deleted:
        Added    → one AuditEntry(Created)
        Deleted  → one AuditEntry(Deleted)
        Modified → one AuditEntry(FieldChanged) per property whose value actually changed
      context.Set<AuditEntry>().AddRange(entries)   ← same context, same transaction
  → TenantSaveChangesInterceptor verifies every row, including the audit rows
  → SQL
```

Points worth being able to explain:

- **Opt-in by attribute.** `[Audited]` sat on the seven domain entities
  that carried financial meaning at the time (nine now). The audit table itself and Identity's tables
  are not audited. A new entity is audited by adding one attribute.
- **Same transaction.** The audit rows are added to the context *before*
  the save proceeds, so a change and its audit row commit together or not
  at all. There is no "audit later" queue that can be lost.
- **Who and when** come from `ICurrentUser` (the claims) and `TimeProvider`
  (injectable, so tests can freeze the clock). Seeding is attributed to
  `system`.
- **Values are text, invariant, and truncated.** `0.00` for money, ISO for
  dates, enum names. They are for people reading history, not for replay.
- **Append-only.** `AuditEntry` has no update methods and no service
  exposes one. Deleting history is not a feature.
- **Interceptor order matters.** Audit runs first so the rows it adds are
  then checked by the tenant interceptor. Registered in that order in
  `Infrastructure/DependencyInjection.cs`.

`AuditKind.Event` exists for Phase 4: "Proposed", "Adopted with resolution
2026-14", "Published" are actions a field diff alone would not explain.

The history screen (`LineHistory.razor`) is a query, newest first, through
`IAuditQueryService`. Audit rows are tenant-owned, so the same query filter
that protects budget data protects history.

---

## 5. The bug that taught EF Core change tracking

Adding a line through the aggregate (`version.AddLine(...)` then
`SaveChangesAsync`) threw `DbUpdateConcurrencyException: expected 1 row,
affected 0`. EF had tracked the *new* line as **Modified**.

Why: the entities assign their own Guid v7 in the constructor. EF's default
for Guid keys is "generated on add". When `DetectChanges` discovers a new
entity through a navigation from a tracked parent, it asks "is a generated
key already set?" and, since it was, concluded the row must already exist
and issued an UPDATE.

Fix: `CivicBudgetDbContext.UseClientGeneratedKeys` marks every domain
entity's `Id` as `ValueGeneratedNever()`. No schema change (the probe
migration was empty), but EF now tracks discovered children as Added. This
is a one-paragraph interview answer about the difference between
`Add`/`Attach`/`Update` and graph discovery.

---

## 6. Dense grids in Blazor: what actually mattered

- QuickGrid's default theme applies cell padding with higher CSS
  specificity than page styles. Passing `Theme="bootstrap"` (any name but
  `default`) turns its styling off and lets Bootstrap's `.table-sm` and
  `app.css` control the layout. Sorting still works; the header button just
  needs a few lines of CSS back.
- A justification column as an always-present input costs ~190 px per row.
  It became a per-row "Add/Note" toggle that opens an editor only for the
  row being edited.
- `AmountCell` parses `"1,250.75"` and `"$1,250.75"`, rejects negatives, and
  only fires its callback on change, so typing never round-trips the
  circuit.
- `.table-responsive` around the grid means any remaining overflow scrolls
  inside the grid, never the page.

---

## 7. Things deliberately not done yet

- Workflow transitions and the Block/Warn *enforcement* (Phase 4).
- Explicit `AuditKind.Event` rows (Phase 4, with the workflow).
- Import of lines (Phase 6). The add-line form is for one-offs.
- Optimistic concurrency between two people editing the same budget at
  once. At this point the last write won. Phase 18 added it, on the budget
  version rather than the line, because an edit racing an adoption is the
  case that matters (walkthrough 20).
