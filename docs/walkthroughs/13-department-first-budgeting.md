# Walkthrough 13: Department-first budgeting

Phase 9d, the last step of v1.1. The fire chief signs in and is standing in
the fire department: every account across its funds, last year's actual,
this year's budget, a request column to type into, a running total, and a
box for the narrative. When the request is ready the chief submits it to
the fiscal officer, who sees on one board which departments are in and can
send one back with a note.

## 1. The domain: a request per department, inside the version

`BudgetVersion` already owned lines and beginning balances; it now owns
`DepartmentRequest` rows too (`Domain/Budgets/DepartmentRequest.cs`). One
per department that has written a narrative or submitted; a department
with no row is simply in progress. Three states:

| Status | Meaning | Who moves it |
|---|---|---|
| `InProgress` | still entering | (default) |
| `Submitted` | handed to the fiscal officer; locked for department users | the department (or the officer on its behalf) |
| `Returned` | sent back with a note; open again, note shown | the fiscal officer |

The rules sit on the aggregate, next to the workflow rules they relate to:

- `SubmitDepartment` needs a **Draft** version (once proposed, the officer
  owns the whole budget) and **at least one line** in the department.
  Submitting twice throws.
- `ReturnDepartment` needs a submitted request and a non-blank note; the
  note is validated before anything mutates, so a bad note cannot leave the
  request half returned. Submitting again clears the note.
- `SetDepartmentNarrative` follows `EnsureEditable` (Draft or Proposed).
- `CreateAmendment` copies narratives (they still describe the year) with
  status reset to `InProgress`: a new round, same words to start from.

`DepartmentRequestTests` covers each of these, including the ordering bug
the tests caught on the first pass (status set before the note was checked).

## 2. One rule, one more argument

`BudgetLinePermissions.CanEdit` is still the single answer to "may this
user edit this line". It gained `departmentSubmitted`: a department user's
line is editable only while Draft, in their department, **and not
submitted**. The fiscal authority ignores the flag. Two siblings,
`CanSubmitDepartment` and `CanReturnDepartment`, answer the two new
actions. `BudgetLineEditHandler` passes the flag through
`BudgetLineResource` with a default, so nothing else had to change.

`BudgetEntryService` answers the flag from the aggregate
(`version.IsDepartmentSubmitted(line.DepartmentId)`) in one private
`CanEdit` helper used by the workspace, amount and note edits, and Add
line. `CanAddLines` is false for a department user whose every department
is submitted.

## 3. The workspace carries the round

Rather than a second read model, `BudgetWorkspaceDto` gained
`DepartmentRequests`: one `DepartmentRequestDto` per department the user
can see (their own for a department user; every active department for the
officer, even those with nothing entered), with status, who submitted and
when, the return note, the narrative, line count, expenditure totals, and
three flags decided for the current user: `CanEditNarrative`, `CanSubmit`,
`CanReturn`. Four screens read that one shape:

- the **department entry page** (`DepartmentEntry.razor`),
- the **department board** (`DepartmentBoard.razor`),
- the **strip** above the workspace grid (`DepartmentStrip.razor`),
- the **Department Budget Detail** report, which prints the narrative under
  each department heading (`ReportBuilder.DepartmentDetail` stays pure).

`IDepartmentRequestService` is the write side: `SaveNarrativeAsync`,
`SubmitAsync`, `ReturnAsync`. Each re-checks the permission against the
current aggregate, calls the domain method, and writes a named audit event
on the version ("Police submitted its budget request", "Returned Parks &
Recreation's budget request: ..."). The interceptor records the field
changes on the request row itself.

## 4. Landing in your department

`Login.razor` now redirects to `admin` instead of the home page.
`Admin/Index.razor` sends a department user on to `/admin/my-department`
(`MyDepartment.razor`), which finds the version in progress (the newest
one not adopted) and navigates to the department page when the user holds
exactly one department, or to the board when they hold several. No open
version: a plain message with a link to the versions list. The sidebar
shows **My department** in place of **Overview** for that role.

## 5. The entry page

`/admin/budgets/{version}/departments/{department}`. It loads the
workspace (already scoped by the service) and filters to one department:

- A card per fund with the department's appropriation lines as full
  account numbers, FY-2 actual, FY-1 budget, FY request (an `AmountCell`
  when `CanEdit`), change, and a row menu for note, history, remove.
- A fund total row and a **department total** bar under the cards.
- Revenue credited to the department (fines, fees) in its own small table,
  because it belongs to the fund's estimated resources, not to the
  department's appropriation total.
  (At this point transfers out still counted in the department's total on this
  page, while the report counted expenditures only. Phase 18 researched the
  question and settled on expenditures only everywhere; walkthrough 20.)
- The narrative editor with a character count; the Submit dialog saves
  unsaved narrative text first, then submits.
- A returned request shows the officer's note in a warning alert; a
  submitted one shows who and when in a success alert and hides the editor.
- The officer sees a **Return to department** button and a link to the
  whole workspace; a department user sees neither.

The page reuses `AddLineForm` with a new `FixedDepartment` parameter, so
adding a line from here cannot land in another department.

## 6. The board and the strip

`/admin/budgets/{version}/departments` lists every department the user can
see with lines, current budget, request, change, status (pill plus "Chief
Hale, 5 days ago" or the return date), and a row menu with Open and, when
`CanReturn`, Return with a note dialog. The workspace shows the same data as
a strip of chips ("2 of 8 submitted", a dot colored by status) while the
version is Draft.

## 7. Published narratives

`PublishedBudgetSnapshot.Capture` now writes one
`PublishedBudgetSnapshotDepartment` per department with lines, carrying the
narrative frozen at publish time. `PublicPortalDbContext` maps the table
with the same Active-only filter; the portal's department page shows it as
"From the department". The seed writes narratives on FY2026 before
adoption, so they travel through the amendment into the published
snapshot; FY2027 is left mid-round for the demo (Police submitted, Parks
returned, the rest in progress).

## 8. Things to read

1. `Domain/Budgets/DepartmentRequest.cs` and the department section of `BudgetVersion.cs`
2. `Application/Security/BudgetLinePermissions.cs` (the new argument and the two new rules)
3. `Application/Budgets/BudgetEntryService.cs` (`ToRequestDto`, `CanEdit`) and `DepartmentRequestService.cs`
4. `Web/Components/Admin/Budgets/DepartmentEntry.razor` and `MyDepartment.razor`
5. `tests/CivicBudget.IntegrationTests/DepartmentRequestServiceTests.cs`
