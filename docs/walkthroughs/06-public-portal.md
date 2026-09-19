# Walkthrough 06 — The public transparency portal

What Phase 5 built and how to explain it. The portal is the one part of
CivicBudget a citizen (or an interviewer) sees without logging in, so it is
where the architectural choices from Phase 0 finally pay off: static SSR,
the snapshot boundary, and output caching.

---

## 1. Start at the URL

```
/transparency                                   every government with a published budget
/transparency/{slug}                            latest published year
/transparency/{slug}/{year}                     overview: KPIs, by fund, by revenue source
/transparency/{slug}/{year}/spending            where it goes: by fund, by category, by department
/transparency/{slug}/{year}/revenue             where it comes from
/transparency/{slug}/{year}/funds               every fund with its balance arithmetic
/transparency/{slug}/{year}/funds/{fund}        one fund: resources, appropriations, ending balance
/transparency/{slug}/{year}/funds/{fund}/departments/{dept}
/transparency/{slug}/{year}/years               year over year
/transparency/{slug}/{year}/search?q=police
/transparency/{slug}/{year}/download.csv|.xlsx  every line, for a spreadsheet
```

Every page is a plain GET. There is no login, no cookie, no SignalR
circuit, and no JavaScript: the `$ | %` toggle on the charts is two links,
the table twins are `<details>`, search is a GET form. Turn JavaScript off
and nothing changes.

Pages live in `src/CivicBudget.Web/Components/Portal/`. The folder's
`_Imports.razor` gives them `@layout PortalLayout` and
`[ExcludeFromInteractiveRouting]`, which is the switch that makes them
static SSR (compare the admin pages, which each declare
`@rendermode InteractiveServer`).

---

## 2. Static SSR versus Interactive Server, in practice

| | Admin (`Components/Admin`) | Portal (`Components/Portal`) |
|---|---|---|
| Render mode | `@rendermode InteractiveServer` | `[ExcludeFromInteractiveRouting]` (static SSR) |
| Per-visitor server state | A circuit: component tree + DI scope + WebSocket | None; the request ends when the HTML is sent |
| Interactivity | `@onclick`, `EditForm`, live fund-balance panel | Links, `<details>`, a GET form |
| Data source | `CivicBudgetDbContext` (tenant-filtered, all tables) | `PublicPortalDbContext` (snapshot tables only, Active only) |
| Authorization | Policies on every page | Anonymous by design |
| Cacheable | No (personal, stateful) | Yes: 6 hours server side, 10 minutes in the browser |

The talking point: a circuit costs memory per connected user, which is
fine for twenty finance staff and wrong for twenty thousand citizens after
a newspaper article. Static SSR turns each visit into an HTTP request that
a cache can answer.

One consequence worth knowing: a static SSR component cannot use
`NavigationManager.NavigateTo` to redirect after a POST, and its
parameters bind once. The portal never needed either. The layout reads the
URL with `NavigationManager.Uri` (`PortalRoutes.Parse`) because layouts do
not receive route parameters.

---

## 3. The read model: `ISnapshotQueryService`

`src/CivicBudget.Application/Portal/ISnapshotQueryService.cs` is the whole
surface the portal reads through, and
`src/CivicBudget.Infrastructure/Portal/SnapshotQueryService.cs` is the only
class that touches `PublicPortalDbContext`.

The implementation is deliberately simple: `LoadAsync` loads one snapshot
with its lines and funds (about a hundred rows for a village) and the
breakdowns are LINQ `GroupBy` in memory. With output caching in front, each
cached page costs one query per six hours; there is no reason to push the
grouping into SQL until a county with thousands of lines shows up, and the
interface would not change if it did.

What the DTOs encode (`PortalDtos.cs`):

- `PortalBudgetDto.ProjectedEndingBalance` = beginning balances + revenues
  + transfers in - expenditures - transfers out, across all funds.
- `PortalFundDto.EstimatedResources` / `Appropriations` /
  `ProjectedEndingBalance` are the Ohio certificate-of-resources arithmetic
  per fund, the same rule `FundBalancePanel` enforces in the admin app.
- `BreakdownDto.ShareOf(item)` gives the percentage for the `%` view.
- `BreakdownItemDto.ChangePercent` uses `Money.PercentChange`, which returns
  null on a zero baseline so the table shows "n/a" instead of dividing by zero.

Because the snapshot carries fund and department names and descriptions
(ADR-0005), none of this joins to the live tables. The integration tests
in `SnapshotQueryServiceTests` pin the arithmetic to the seeded snapshot and
prove that unpublishing removes a government from every query at once.

---

## 4. Charts without a chart library

`Components/Portal/Common/Breakdown.razor` is the one visualization. It is a
CSS grid of horizontal bars, each with its label and its value in text, a
`role="img"` summary for screen readers, and a `<details>` element with the
same numbers as a table (amount, share, change versus prior year). The
longest bar is 100% wide; the rest scale to it.

Why not Chart.js? Three reasons that come up in transparency work: the
numbers must be readable without color and without JavaScript, the page
must print, and a bar chart with labels beside the bars is what every
serious portal (Ohio Checkbook, OpenGov) converges on anyway. The bUnit
tests in `BreakdownTests` check the accessibility promises directly: label
and value per bar, the table twin, the toggle as links.

`MoneyShort` formats the KPI cards ("$3.66M", "$588K"); full precision is
always one click away in a table.

---

## 5. Output caching (ADR-0021)

The pieces are in `src/CivicBudget.Web/Caching/` and wired in `Program.cs`:

```csharp
builder.Services.AddOutputCache(options =>
    options.AddBasePolicy(policy => policy.AddPolicy<PortalOutputCachePolicy>(), excludeDefaultPolicy: true));
builder.Services.AddSingleton<IPublishedSnapshotCacheInvalidator, OutputCacheSnapshotInvalidator>();
...
app.UseMiddleware<PortalResponseMiddleware>();   // before UseOutputCache so it runs on hits too
app.UseOutputCache();
```

What each does:

1. **`PortalOutputCachePolicy`** decides per request. `GET /transparency/**`
   is cached for six hours, keyed by path and query, tagged
   `portal:{slug}`. It refuses to store a non-200 (a not-found for a
   government published later must not stick) or anything that still sets
   a cookie. Every other path is left alone.
2. **`PortalResponseMiddleware`** fixes the headers Blazor writes. Static
   SSR marks every page `no-cache, no-store` and sets an antiforgery
   cookie; both are correct for the admin app and wrong for the portal. In
   `OnStarting` (the last moment headers are writable) it sets
   `Cache-Control: public, max-age=600` and removes the antiforgery
   `Set-Cookie`. It sits before the output cache so the stored copy and the
   fresh copy get the same treatment.
3. **`OutputCacheSnapshotInvalidator`** evicts by tag. `PublishingService`
   already called `IPublishedSnapshotCacheInvalidator` after every publish
   and unpublish (Phase 4 registered a no-op); Web now registers the real
   one after `AddInfrastructure`, so the last registration wins.

Things that went wrong on the way, all now in `CLAUDE.md` gotchas: the
default policy silently refused to cache anything (`no-store`), response
headers are read-only inside `ServeResponseAsync`, and a middleware placed
after `UseOutputCache` never runs on a hit.

Verify with curl:

```bash
curl -sI http://localhost:5000/transparency/maple-ridge-oh | grep -i -E "cache-control|set-cookie|age:"
```

First call: `Cache-Control: public, max-age=600`, no cookie. Second call:
the same plus `Age: 0`. Publish an amendment in the admin app and the next
call renders fresh.

---

## 6. Downloads

`PortalEndpoints.cs` maps two minimal API endpoints under the same
`/transparency/{slug}/{year}` prefix. Both ask `ISnapshotQueryService` for
every line, build an `ExportTable` (name, headers, rows of typed cells),
and hand it to `CsvWriter.ToCsv` (Application, no package) or
`ISpreadsheetExporter.ToXlsx` (Infrastructure, ClosedXML). The endpoints
are GETs under the portal prefix, so the output cache stores the files
too, tagged by government like the pages.

`CsvWriterTests` and `ClosedXmlSpreadsheetExporterTests` cover the details
that make a download open cleanly in Excel: UTF-8 BOM, CRLF, quoting,
invariant number formats, typed cells with a frozen bold header.

---

## 7. Accessibility

- Landmarks: `<header>`, `<nav aria-label>`, `<main>`, `<footer>`;
  breadcrumbs as an ordered list; year pills with `aria-current="page"`.
- Every chart has a text alternative (the bar labels and values, the
  `role="img"` summary, and the table twin).
- Color is never the only signal; bars carry their value beside them.
- Contrast comes from the same tokens as the admin app (checked for AA).
- Works with JavaScript disabled and at 390px (no horizontal scroll; wide
  tables scroll inside their own wrapper).
- A footer accessibility statement names the target (WCAG 2.1 AA) and how
  to report a problem; the screen-reader pass is scheduled for Phase 8.

---

## 8. Things to read, in order

1. `Components/Portal/PortalOverview.razor` and `PortalLayout.razor` (the shape of a static SSR page)
2. `Application/Portal/ISnapshotQueryService.cs` and `PortalDtos.cs` (the contract)
3. `Infrastructure/Portal/SnapshotQueryService.cs` (the only reader of the portal context)
4. `Web/Caching/*.cs` and the three lines in `Program.cs` (ADR-0021)
5. `tests/CivicBudget.IntegrationTests/SnapshotQueryServiceTests.cs` and `tests/CivicBudget.Web.Tests/Portal/*`
