# Demo script (about ten minutes)

What I show, in what order, and what I say at each stop. It works on the
[live demo](../README.md#try-the-live-demo) or locally
(`docker compose up -d && dotnet run --project src/CivicBudget.Web`) with fresh seed data.
If the live demo has been idle, open it a minute early so the database is awake. Keep
[INTERVIEW-PREP.md](INTERVIEW-PREP.md) open in another tab for follow-up questions.

## 0. Frame it (30 seconds)

"Ohio's local governments build an appropriation budget every year, fund by fund, and
the law says appropriations can't exceed each fund's certified estimated resources.
CivicBudget is that workflow, for several governments at once, plus the public portal
citizens see at the end. It's built to sit beside the government's ERP rather than
replace it. .NET 10, Blazor, EF Core, SQL Server. All the data is fictional."

## 1. The public portal, before signing in (1.5 minutes)

Open `/transparency/maple-ridge-oh`.

- "This is plain server-rendered HTML: no sign-in, no SignalR connection, no JavaScript.
  The `$ | %` toggle is two links, and every chart has a table behind it."
- Click **2011 Street Construction** for the fund page. "Estimated resources,
  appropriations, projected ending balance: the Ohio certificate arithmetic, per fund."
- Open a department, such as Police. "The department's own budget message is published
  with its numbers."
- Point at the year pills. "Every published year. The portal only ever reads frozen
  snapshots of adopted budgets; there's no code path to a draft."
- Optional: the browser's network tab, reload, and show `Cache-Control: public,
  max-age=600`. "Cached per government; publishing clears that government's pages."

## 2. The fiscal officer's workspace (2.5 minutes)

Sign in as `finance@mapleridge.example`, then **Continue FY2027** on the overview.

- "The admin side is interactive, because staff need live grids. One connection per
  finance user is fine; one per citizen wouldn't be, which is why the portal isn't."
- Change an amount; watch the fund balance panel update. "It saves through an
  application service, and an interceptor writes the audit row in the same transaction."
- Row menu, **History**. "Who changed what, when, from what to what."
- Point at the chips above the grid: "1 of 8 submitted. Each department enters its own
  request and hands it in; this is where the fiscal officer sees who's in." Click **All
  departments** for the board: Police submitted, Parks returned with a note, the rest
  still entering.
- Hover the ⓘ beside the title. "Explanations live behind these, so the screen stays a
  working screen."

## 3. The police chief's view (1 minute)

In a private window, sign in as `police@mapleridge.example`. "No overview and no other
departments: the chief lands in the police department for the budget in progress."
Every police account across its funds as full account numbers, the request column, the
running total, the narrative. It's already submitted, so it's locked. Back in the
officer's window, return it with a note, then refresh the chief's page: the note is
there and the amounts are editable again. "One rule decides who may edit a line; the
fiscal officer is never locked out."

## 4. The limit and the workflow (1.5 minutes)

- Point at the fund balance panel: the Street fund (2011) is red. "It appropriates about
  $36,000 more than it expects to have. This government is in Block mode, so the workflow
  refuses to move." Click **Propose to council** and show the refusal.
- Raise the Street fund's beginning balance by $40,000 (or cut its infrastructure line)
  and Propose goes through. "Staff can save a budget that's out of balance while they
  work; the rule is enforced at the step where it matters."
- **Adopt** with a resolution number, then **Publish to portal**. Open the portal in a new
  tab: FY2027 is there.
- "Adopting makes the version immutable in the domain. Publishing copies it into a
  snapshot with its own names and codes, so renaming an account next year can't change
  what citizens saw this year."

## 5. Import, reports, and next year (2 minutes)

- Tools, **Export lines (XLSX)**. "The same columns the import reads: export, edit in
  Excel, import."
- Tools, **Import lines from a file**, with a prepared CSV that has one update, one new
  line, and one bad account code. "Every row is classified, nothing is written until you
  confirm, and the button won't enable with an error in the file. Commit checks again
  against the database at that moment."
- Reports, **Budget Summary by Fund**, then **Print**. "Built from the same read as the
  workspace, so a report can't disagree with the screen. Printing is a stylesheet."
- Setup, **Fiscal years**: add FY2028 and choose **Start the budget**, from FY2027's
  adopted budget, +3% on appropriations only. "Last year's adopted amount becomes the
  comparison column, and each fund starts with last year's projected ending balance."

## 6. The code, briefly (2 minutes)

In the editor, in this order:

1. `Domain/Budgets/BudgetVersion.cs`: `AddLine`, `Adopt`, `CreateAmendment`,
   `CreateOriginalFrom`, and `Touch()`. "The rules live here, not in the pages."
2. `Infrastructure/Persistence/CivicBudgetDbContext.cs`: the tenant query filter loop.
3. `Infrastructure/Persistence/PublicPortalDbContext.cs`: the snapshot tables and the
   logo, nothing else, and `SaveChanges` throws.
4. `Web/Startup/WakingUpMiddleware.cs`: "how the site answers in seconds while a sleeping
   database wakes up."
5. `infra/CivicBudget.Infra/CivicBudgetStack.cs`: "deploy-ready on AWS, synthesized and
   tested in CI."

Close with the numbers: four projects plus tests and infrastructure, about 640 tests
including a real SQL Server in Docker, and every phase a pull request with a walkthrough.

## If something goes wrong

- The live demo shows "Waking up the demo": wait; it continues on its own within a minute.
- Locally, SQL Server exited under Rosetta: `docker compose up -d` brings it back, and the
  data survives.
- You need fresh data: `docker compose down -v && docker compose up -d`, then run the app
  (locally), or wait for the nightly reset (live).
