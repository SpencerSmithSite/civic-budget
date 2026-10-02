# Demo script (about twelve minutes)

What I show, in what order, and what I say at each stop. It works on the
[live demo](../README.md#try-the-live-demo) or locally
(`docker compose up -d && dotnet run --project src/CivicBudget.Web`) with fresh seed data.
If the live demo has been idle, open it a minute early so the database is awake.

## 0. Frame it (30 seconds)

"Ohio's local governments build an appropriation budget every year, fund by fund, and
the law says appropriations can't exceed each fund's certified estimated resources.
CivicBudget is that workflow, for several governments at once, plus the public portal
citizens see at the end. It plans five years ahead, and it has an AI assistant, Civic
Buddy, that works only as the signed-in user and changes nothing without a click. It's
built to sit beside the government's ERP rather than replace it. .NET 10, Blazor, EF Core, SQL Server. All the
data is fictional."

## 1. The public portal, before signing in (1.5 minutes)

Open `/transparency/maple-ridge-oh`.

- "This is plain server-rendered HTML: no sign-in, no SignalR connection, no JavaScript.
  The `$ | %` toggle is two radio buttons that CSS reads, and every chart has a table behind
  it."
- Click **2011 Street Construction** for the fund page. "Estimated resources,
  appropriations, projected ending balance: the Ohio certificate arithmetic, per fund."
- Open a department, such as Police. "The department's own budget message is published
  with its numbers."
- Point at the year pills. "Every published year. The portal only ever reads frozen
  snapshots of adopted budgets; there's no code path to a draft."
- **Outlook**: "The five-year plan is published with the budget: each year's totals and each
  fund's balance, with the assumptions shown, and a plain line that these years are a plan."
- **Ask Civic Buddy**, the panel in the corner of every page: "It says it's an AI up front, and
  it opens with no JavaScript; it's a `<details>` element." Ask "How much is budgeted for police, and how does it compare to last
  year?" The answer quotes the published figures and links the Police page. Then ask "How much
  has the village actually spent so far this year?" "It says that isn't published. Its tools
  read the published snapshot and nothing else, so it can't leak a draft or the ERP's books,
  and it works with JavaScript off."
- Optional: the browser's network tab, reload, and show `Cache-Control: public,
  max-age=600`. "Cached per government; publishing clears that government's pages."

## 2. Civic Buddy, the assistant (2.5 minutes)

Sign in as `finance@mapleridge.example` and open **Civic Buddy** in the top bar.

- Ask "How are actuals compared to our budget so far this year?" "It looked that up in the
  same budget-against-actual report the Reports page shows, as me, and says so under the
  answer. The model never sees the database."
- Open the FY2027 draft and ask "Check my budget before I propose it." "What must be fixed
  first: the Street fund is over the Ohio limit. Then what council will ask about: departments
  not in, changes with no justification."
- Ask "Fix the Street fund." "A card: every spending line in that fund, cut in proportion to
  land exactly on the limit. The arithmetic is code, not the model. Nothing has changed." Click
  **Cancel** (the red fund is needed in section 6).
- Ask "Raise utilities 5% in this budget." Check the card against the grid, click **Change 6
  lines**, and the grid shows the new amounts. Row menu, **History** on one of them: "The
  change is in the audit trail under my name, and a separate audit entry records that
  Civic Buddy proposed it. If someone had changed a line since the preview, nothing would change
  and it would say which."
- Optional, in a private window as `police@mapleridge.example`: ask for Finance's budget. "It
  can't see it, because the chief can't."

## 3. Five years ahead (1.5 minutes)

- Budget versions, FY2027, **Plan**. "Every budget carries a plan: a revenue and a spending
  percentage per year, balances rolled forward, the Ohio limit checked every year." Set 4% and
  3% under **Same change every year**, **Use for every year**, **Save the plan**: every fund's
  ending balance moves. "Nothing projected is stored, so the plan can never disagree with the
  budget."
- Reports, pick **FY2026 Amendment 1**, **Revenue vs. Receipts**. "Real estate taxes are 96%
  collected in August, and that is normal: last year it was 95% by now. A straight line would
  call that ahead; income tax at 67% against 65% is the one to watch." Then **Projected Fund
  Balances**: "Each line projected from last year's pattern, not a straight line."

## 4. The fiscal officer's workspace (2 minutes)

Still as `finance@mapleridge.example`, **Continue FY2027** on the overview.

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
- Open **Police**: the salary and benefit lines say "from 9 positions". Click one. "Nobody
  types these. Each position is priced: pay scale, step increase in its anniversary month,
  longevity from the FOP contract, OP&F, Medicare, workers' comp, insurance by tier." Open
  the vacant patrol officer and change its first month; the breakdown reprices as you type.
  Save, and the department's lines move.
- **Personnel settings** under Setup: "Settings belong to a year. Longevity reads back in
  plain English, so it can be checked against the contract."
- **Employees from the ERP**, **Fetch employees**: "Nobody types the roster either. The
  payroll has moved on since the budget started: a hire fills the police vacancy, a clerk
  had a raise, a street worker retired. Here is every line that moves." Apply.
- **Reports**, Personnel: the roster, cost by fund, and the benefits summary. "The roster's
  total is the personnel lines to the cent."
- Sign out; **Forgot your password?** for `viewer@mapleridge.example`. Sign in as the
  administrator and open **Email outbox**: "The demo keeps its mail here, since every address is
  fictional. In production it goes out over SMTP, written in the same save as the change that
  caused it." Click the link to show the reset works.
- **Your account**, **Two-step sign-in**: scan the QR code with a phone. "Any authenticator app.
  An administrator can require it for everyone under Government settings."
- **Getting started**: "A new government is set up with one command, and its administrator
  follows this list, worked out from what exists."

## 5. The police chief's view (1 minute)

In a private window, sign in as `police@mapleridge.example`. "No overview and no other
departments: the chief lands in the police department for the budget in progress."
Every police account across its funds as full account numbers, the request column, the
running total, the narrative. It's already submitted, so it's locked. Back in the
officer's window, return it with a note, then refresh the chief's page: the note is
there and the amounts are editable again. "One rule decides who may edit a line; the
fiscal officer is never locked out."

## 6. The limit and the workflow (1.5 minutes)

- Point at the fund balance panel: the Street fund (2011) is red. "It appropriates
  $21,908.68 more than it expects to have. This government is in Block mode, so the workflow
  refuses to move." Click **Propose to council** and show the refusal.
- Raise the Street fund's beginning balance by $40,000 (or cut its infrastructure line)
  and Propose goes through. "Staff can save a budget that's out of balance while they
  work; the rule is enforced at the step where it matters."
- **Adopt** with a resolution number, then **Publish to portal**. Open the portal in a new
  tab: FY2027 is there.
- "Adopting makes the version immutable in the domain. Publishing copies it into a
  snapshot with its own names and codes, so renaming an account next year can't change
  what citizens saw this year."

## 7. Import, reports, and next year (1.5 minutes)

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
- Setup, **Actuals sync**: fetch FY2025 from ERP (simulated). "The ERP's closed books.
  Every prior-year actual in the FY2027 draft already matches, so nothing changes; if one
  had been typed over, the preview would list it." Then open a department page: "This
  year's spending sits under each budget, with a bar that turns amber past it."
- Budget versions, **FY2026 Amendment 1**, **Send to ERP**. "The originals went to the ERP in
  January, so the journal is only the supplemental appropriation: two lines, +$21,000." Send it,
  then show the page now says the ERP already has the budget. "Pressing it again does nothing. A
  lost answer is retried under the same id, and a downloaded file only counts once someone
  confirms it was imported."
- Reports, **Certificate of Estimated Resources** on FY2026 Amendment 1. "The legal ceiling on
  each fund. The balance is the ERP's year-end cash less carried encumbrances, and it equals the
  budget's beginning balance, so every reconciliation passes." Show the detailed schedule, then
  **Download PDF**. Switch to FY2027: "Before the ERP closes 2026 it uses the budget's estimate,
  and says so; and the Street fund is over its total." Setup, **Report settings**: "Which
  accounts are taxes is the village's choice, and the headings follow the county's template."

## 8. The code, briefly (2 minutes)

In the editor, in this order:

1. `Domain/Budgets/BudgetVersion.cs`: `AddLine`, `Adopt`, `CreateAmendment`,
   `CreateOriginalFrom`, and `Touch()`. "The rules live here, not in the pages."
2. `Infrastructure/Persistence/CivicBudgetDbContext.cs`: the tenant query filter loop.
3. `Infrastructure/Persistence/PublicPortalDbContext.cs`: the snapshot tables and the
   logo, nothing else, and `SaveChanges` throws.
4. `Web/Startup/WakingUpMiddleware.cs`: "how the site answers in seconds while a sleeping
   database wakes up."
5. `Application/Assistant/ActionTools.cs`: "every action proposes; only the card's button,
   through `AssistantService.ConfirmAsync`, commits, and it goes through the page's own service."
6. `Application/Assistant/PortalTools.cs`: "the public bot's tools are the portal's own reads,
   for one government, and none of them takes a government."
7. `infra/CivicBudget.Infra/CivicBudgetStack.cs`: "deploy-ready on AWS, synthesized and
   tested in CI."

Close with the numbers: four projects plus tests and infrastructure, about 1,175 tests
including a real SQL Server in Docker and an evaluation set run against the real model, and
every phase a pull request with a walkthrough.

## If something goes wrong

- The live demo shows "Waking up the demo": wait; it continues on its own within a minute.
- Locally, SQL Server exited under Rosetta: `docker compose up -d` brings it back, and the
  data survives.
- You need fresh data: `docker compose down -v && docker compose up -d`, then run the app
  (locally), or wait for the nightly reset (live).
- The Civic Buddy button is missing: no model is connected. Locally, set `Assistant:ApiKey` (and
  `Assistant:Provider` and `Assistant:Model` for Ollama) in the web project's user-secrets; on
  Azure, run `./scripts/azure-assistant.sh`. An answer takes a few seconds on the demo's small
  model.
