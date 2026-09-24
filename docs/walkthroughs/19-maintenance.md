# Walkthrough 19: Maintenance pass

Phase 17. No new features: a review of every layer, a walk through the running
app as every demo user, and about ninety fixes. This is the phase to read for
"how do you find bugs in a codebase you think is finished?"

## 1. How the review ran

The code was reviewed one layer at a time: domain and application;
infrastructure and server plumbing; the admin Blazor pages; the shared UI,
portal, account pages, and CSS; and the tests, CI, Docker, and infrastructure
scripts. Every finding had to quote the lines and give a concrete failure
scenario, and anything uncertain was marked as such. About 130 findings came
out. None was trusted as written: each fix started by reading the code again,
and the top admin findings were reproduced in a browser first.

Alongside that, `scripts/screenshots/role-sweep.mjs` signed in as each of the six
demo users and opened every page they can reach (about 220 page loads),
recording console errors, failed requests, the Blazor error bar, and text such
as "NaN". It found nothing. That was the lesson of the pass: pages that load
cleanly can still be wrong the moment someone clicks something.

## 2. The bugs that only appear when you act

Three Blazor behaviors explain most of the admin bugs.

**A `Func` callback does not re-render its caller.** `ConfirmDialog.OnConfirm`
and `AddLineForm.OnAdd` are `Func<Task<...>>`, not `EventCallback`. Blazor
re-renders the component whose handler ran (the dialog), not the page that
owns the data. Five list pages reloaded their data after Deactivate, Lock, or
Close and never showed it; Add line added a line nobody could see until a
reload. Each now calls `StateHasChanged()` after reloading, with a comment
saying why, and `FundListTests` has a regression test that fails without it.

**A page is reused when only its route parameter changes.** "Start an
amendment" navigates from `/admin/budgets/{adopted}` to
`/admin/budgets/{draft}`. Same component type, so Blazor keeps the instance and
`OnInitializedAsync` does not run again: the URL said draft, the page said
Adopted. `BudgetWorkspace` and `DepartmentEntry` now load in
`OnParametersSetAsync`, guarded by the id they last loaded.

**Blazor diffs against what it rendered, not what is in the browser.** An
amount cell rendered `value="1,000.00"`. The user typed 2500, the save was
refused, and `Value` stayed 1000, so the next render produced the same
attribute and Blazor sent nothing: the input kept "2500" while every total
around it said 1,000. `AmountCell` now bumps an `@key` when the saved value
differs from what was typed, which renders a fresh input.

A fourth, QuickGrid-specific: its `Paginator` re-renders only itself and the
grid, so the phone cards (which read the same `PaginationState`) stayed on page
one. `ListPager` does the same job and tells the page when the page changes.

## 3. Security

- **Open redirect.** `Uri.IsWellFormedUriString("//evil.example", Relative)`
  is true, and a browser treats `//evil.example` as another host. `LocalUrl`
  follows ASP.NET Core's `IsLocalUrl` rules; `LocalUrlTests` lists the tricks.
- **"Has a dot" is not "is a static file".** Two middlewares let any path with
  a dot through, which included `/admin/export/*.xlsx`: a user with a temporary
  password could download every export. `StaticAssetPath` lists extensions.
- **Uploads.** The logo accepted SVG, served from the app's own origin.
  `UploadedImage` accepts PNG, JPEG, and WebP only, checks the first bytes
  against the declared type, and the image endpoints send `nosniff` and a
  sandboxing CSP.
- **Defense in depth.** Setup services now check the role themselves, and
  `AdminPagePolicyTests` pins every admin page to its policy.
- **Cost as a security property.** The portal cache varied on every query key,
  so `?x=1`, `?x=2`, ... rebuilt pages from the database; and `/health/ready`
  queried the database for anyone. On the free tier both could burn the
  month's database allowance and take the demo down.

## 4. Rules and data

- ERP sync could change an account from revenue to expenditure while adopted
  budgets used it; it now refuses, like the account screen.
- Closed fiscal years accepted amendments; `FiscalYear.EnsureOpenForNewVersions`.
- A return note of the maximum length made the audit event too long *after*
  the domain accepted it; audit text is now shortened like audit values.
- Import rejected CivicBudget's own export (codes are zero-padded on the way
  out) and read `1234,56` as 123,456.
- Renaming a government's public address stranded its published budgets; they
  now move with it and both addresses' cached pages are evicted.
- Three indexes: fund-level lines unique per version (EF Core's unique index
  over a nullable department skips lines that have none), one active snapshot per year,
  and recent activity by government and time.

## 5. Things the fixes taught

- The first seed change found governments by their address. The setup tests
  failed within minutes: rename a government's address and the next start would
  have seeded a second copy (the live demo seeds on every start). Governments
  now seed only into an empty database; users are filled in per email.
- The portal's own not-found page never rendered: .NET 10 discards a static
  page's body once it sets 404. The re-executed `/not-found` page now reads the
  original path and shows the portal's wording, and the portal page hands its
  sentence over in `HttpContext.Items`.
- The per-request portal memo broke two tests that changed data and read it
  back through the same scope. The tests now use a fresh scope, as a real next
  request would.

## 6. Open questions

*Answered and implemented in Phase 18; see walkthrough 20.* These were budgeting
rules, not bugs, so I left the code alone until I had decided each one:

1. **Publishing an older adopted version.** After Amendment 1 is adopted and
   published, the Original can still be published, which replaces the
   amendment on the portal. Should only the latest adopted version be
   publishable?
2. **What a department's total includes.** The by-department grouping in the
   workspace adds every line type, revenue credited to the department
   included; the department page adds expenditures and transfers out; the
   request total and the Department Detail report use expenditures only.
   Which is the department's appropriation total?
3. **Negative prior-year actuals.** Import accepts "(10)" as -10 for prior-year
   actual and current budget; manual entry refuses negatives. Which is right?
4. **Starting a new year's budget.** A fiscal year created in the app has no
   way to get its Original budget version (only the seed creates one). Should
   there be a "Start the FY budget" action, and should it copy last year's
   adopted lines?

## 7. Known gaps, left on purpose

*All five closed in Phase 18; see walkthrough 20.*

- No optimistic concurrency: an amount edit can save after a concurrent
  adoption, and two adoptions can both succeed. A row version on
  `BudgetVersion`, touched by every line change, is the fix (ADR-0032).
- Dialogs return focus but do not trap it, and the page behind is not `inert`.
- The AWS deploy pushes to an ECR repository its own stack creates, so a first
  AWS deploy would fail at the push. There is no AWS account to test against.
- Integration tests migrate and seed a fresh database per test; a template
  database restored per test would roughly halve the run.
- Third-party GitHub Actions are pinned by major version, not by SHA.

## 8. Things to read

1. `Web/Components/Admin/Budgets/AmountCell.razor` (the `@key` reset) and `BudgetWorkspace.razor` (`OnParametersSetAsync`)
2. `Web/Components/Common/ListPager.razor`
3. `Web/Components/Account/LocalUrl.cs` and `Web/StaticAssetPath.cs`
4. `Application/Common/UploadedImage.cs`
5. `tests/CivicBudget.Web.Tests/Security/AdminPagePolicyTests.cs`
