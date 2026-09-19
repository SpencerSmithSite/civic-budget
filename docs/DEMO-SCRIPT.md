# Demo script (about ten minutes)

What to show, in what order, and the sentence to say at each stop. Assumes
`docker compose up -d && dotnet run --project src/CivicBudget.Web` (or the
containerized stack on :8080) with fresh seed data. Have
`docs/INTERVIEW-PREP.md` open in another tab for the follow-up questions.

## 0. Frame it (30 s)

"Local governments in Ohio build an appropriation budget every year, fund
by fund, and are required to keep appropriations inside certified
estimated resources. CivicBudget is that workflow, multi-tenant, plus the
public portal citizens get at the end. .NET 10, Blazor, EF Core, SQL
Server, CDK in C#. Everything you'll see is fictional data."

## 1. The public portal, before logging in (1.5 min)

Open `/transparency/maple-ridge-oh`.

- "This is static server-rendered HTML: no login, no SignalR circuit, no
  JavaScript. The `$ | %` toggle is two links; every chart has a table
  behind `Show as a table`."
- Click **2011 Street Construction** → the fund page. "Estimated resources,
  appropriations, projected ending balance: the Ohio certificate
  arithmetic, per fund."
- Point at the year pills. "Every published year. The portal only ever
  reads immutable snapshots; there is no code path to a draft."
- Optional: DevTools network tab → reload → `Cache-Control: public,
  max-age=600`. "Output-cached by government tag; publishing evicts it."

## 2. Sign in and the workspace (2.5 min)

Sign in as `finance@mapleridge.example`. Overview → **Continue FY2027**.

- "Interactive Server here: staff need live grids. One SignalR circuit per
  finance user is fine; per citizen it would not be, which is why the
  portal is not."
- Change an amount in the grid; watch the fund balance panel update.
  "Saves on blur through an Application service; a `SaveChanges`
  interceptor writes the audit row in the same transaction."
- Row menu → **History**. "Who, what, when, old and new value."
- Switch **By department**. "Same lines, grouped the way a department head
  sees them. A department head signed in sees only their departments;
  that's a resource-based authorization handler, not a filter in the page."
- Hover the ⓘ next to the title. "Explanations live behind these so the
  screen stays a screen."

## 3. The limit and the workflow (1.5 min)

- Fund balance panel → edit the General Fund beginning balance down until
  it goes over. "Block mode: the workflow refuses. Warn mode asks for an
  acknowledgement. That's the government's setting, enforced at the
  transition, not in the UI."
- Put it back. **Propose to council** → **Adopt** with a resolution number
  → **Publish to portal**. Open the portal in a new tab: FY2027 is there.
- "Adopt makes the version immutable in the domain; publish copies it into
  a denormalized snapshot with names, so the portal needs no joins and the
  live tables can change without touching what citizens see."

## 4. Import and reports (2 min)

- Tools → **Export lines (XLSX)**. "Same column layout the import reads:
  export, edit in Excel, import."
- Tools → **Import lines from a file** with a prepared CSV that has one
  update, one new line, and one bad account code. "Every row classified;
  nothing is written until you confirm; the button won't enable with an
  error. Commit re-validates against the database at that moment."
- Reports → **Budget Summary by Fund** → **Print** preview. "Built from the
  same read as the workspace, so a report can't disagree with the screen.
  Print is a stylesheet."

## 5. The code, briefly (2 min)

In the editor, in this order:

1. `Domain/Budgets/BudgetVersion.cs` — `AddLine`, `Adopt`, `CreateAmendment`:
   "The rules are here, not in pages."
2. `Infrastructure/Persistence/CivicBudgetDbContext.cs` — the tenant query
   filter loop and the interceptor order.
3. `Infrastructure/Persistence/PublicPortalDbContext.cs` — three tables,
   `SaveChanges` throws.
4. `Web/Caching/PortalOutputCachePolicy.cs` — twenty lines that make the
   portal cheap.
5. `infra/CivicBudget.Infra/CivicBudgetStack.cs` — "Deploy-ready: synthesized
   and asserted in CI, waiting on an account."

Close with the numbers: five projects, around four hundred tests including
a real SQL Server in Testcontainers, every phase a PR with a walkthrough.

## If something goes wrong

- SQL Server exited under Rosetta: `docker compose up -d` restarts it;
  data survives.
- Signed out unexpectedly after a rebuild: expected once after the Data
  Protection key move; sign in again.
- Need fresh data: `docker compose down -v && docker compose up -d`, then
  run the app.
