# Walkthrough 07: Import, export, and reports

Phase 6 added three things every government ERP has, and where real systems get
messy: files from Excel, files back to Excel, and paper.

---

## 1. Import: preview first, commit second (ADR-0022)

The workspace's Tools menu offers "Import lines from a file" to the Finance
Director on an editable version. The page is
`Components/Admin/Import/BudgetImport.razor`; the service is
`Application/Import/BudgetImportService.cs`.

The shape of the feature is two calls on `IBudgetImportService`:

```
PreviewAsync(versionId, fileName, stream)   -> ImportPreviewDto (rows + counts + the raw inputs)
CommitAsync(versionId, fileName, inputs)    -> ImportResultDto  (added, updated, unchanged)
```

Preview never writes. Commit takes the *raw* rows back (not the preview's
verdicts), re-analyses them against the database at that moment, and
refuses if any row errs. That guards against the obvious race: someone
deactivates an account or adds a line by hand between preview and commit.
The clerk sees the preview again with the new problem instead of a
half-applied file.

When commit does apply, it goes through the aggregate:
`BudgetVersion.AddLine`, `UpdateLineAmount`, `UpdateLineComparatives`,
`UpdateLineJustification`, all in one `SaveChangesAsync`. The audit
interceptor records each field change as it would for a hand edit, and the
service adds one `AuditKind.Event` row ("Imported budget.csv: 1 added, 1
updated, 0 unchanged") so the history view shows the import as a single
act with a name on it.

### The rules, without a database

`ImportAnalyzer.Analyze(rows, funds, departments, accounts, existingLines)`
is a static function over plain records. It resolves codes (case
insensitive), marks unknown and inactive codes, requires a department on
expenditure lines, parses money ("$1,250.50", "(500)"), refuses negative
amounts, flags a key that appears twice in the file, and decides
Add/Update/Unchanged by comparing with the existing line. Blank optional
columns mean "leave as is", so a file with only Amount filled in updates
amounts alone.

Every one of those rules has a test in `ImportAnalyzerTests` with no
container, and `BudgetImportServiceTests` proves the same behaviour over
SQL Server with the seeded draft. The service's job is small: load the
version and the code tables, call the analyser, apply the verdicts.

### The file contract is the export

`ImportFileParser.Headers` is the column list: Fund, Department, Account,
Amount, Prior Year Actual, Current Year Budget, Justification. Codes, not
ids or names, because clerks build these files in Excel from the chart of
accounts they already know. `ReportTables.Lines` writes exactly that layout
for the workspace's "Export lines", so the round trip is: export, edit,
import. The import page's header button downloads it as the template.

Import never deletes: a line that is not in the file is left alone. That
is the safe default for a budget (a partial file from one department must
not wipe another's lines) and it is said on the page.

### Reading the file

`CsvReader` (Application) is an RFC 4180 parser in sixty lines: quotes,
doubled quotes, embedded newlines, CRLF or LF, a UTF-8 BOM. XLSX goes
through `ISpreadsheetReader`, implemented with ClosedXML in Infrastructure
(`ClosedXmlSpreadsheetReader`), which returns numbers as invariant strings
so both formats reach the analyser as text. Both produce a `TabularFile`
(headers and string cells), and `ImportFileParser` maps columns by name in
any order and numbers rows the way the spreadsheet does (header is row 1),
so an error message points at the row the clerk can see.

The Blazor side: `InputFile` streams the upload over the circuit with a
5 MB cap; the page buffers it and hands the bytes to the service. Nothing
about files leaks below the Web project.

---

## 2. Export: one exporter, many tables

Phase 5 introduced `ExportTable` (name, headers, rows of typed cells) and
`ISpreadsheetExporter` (ClosedXML). Phase 6 reuses them from
`Components/Admin/AdminExportEndpoints.cs`, a set of minimal API GETs under
`/admin/export/**`:

- `budgets/{id}/lines.xlsx` (the import layout)
- `reports/{id}/fund-summary.xlsx`, `department-detail.xlsx?department=`, `category.xlsx`
- `setup/funds.xlsx`, `departments.xlsx`, `accounts.xlsx`

They sit behind the same cookie sign-in and policies as the pages
(`RequireAuthorization(Policies.CanViewBudget)`, setup exports behind
`CanMaintainSetup`), and `CurrentUserMiddleware` fills the tenant for a
plain HTTP request the way the circuit handler does for a page, so the
Application services cannot tell a download from a screen. An anonymous
request is redirected to sign in; a wrong tenant gets a 404 because the
query filter hides the version.

`ReportTables` (Application) holds the column mapping for each report next
to its DTO, so adding a column to a report and its spreadsheet is one
change in one folder.

---

## 3. Reports: shaped from the workspace, printed with CSS

`IReportService` returns three DTOs; `ReportBuilder` is a pure function
over `BudgetWorkspaceDto`, the same read the entry screen uses:

| Report | What it shows | Ohio angle |
|---|---|---|
| Budget Summary by Fund | Beginning balance, revenues, transfers in, **estimated resources**, expenditures, transfers out, **appropriations**, projected ending balance, within-limit flag, totals | The certificate arithmetic (§3.9) fund by fund: what the fiscal officer certifies |
| Department Budget Detail | Every line by department with prior year actual, current year budget, this request, $ and % change, subtotals; filter to one department | The pages a department head brings to a council hearing |
| Revenue vs. Expenditure by Category | Revenues by source, expenditures by category, same three comparatives, net at the bottom | The one-page picture for the finance committee |

Building from the workspace DTO means two rules come for free: the tenant
filter, and the department user's visibility rule (their detail report shows
only their departments, and `ReportServiceTests` proves it). It also means
a report can never disagree with the screen next to it, which is the
complaint every finance office has about reporting bolted on later.

The pages (`Components/Admin/Reports/`) share `ReportShell.razor`: the
admin page header with Print and Export XLSX, then a white report block
that leads with the government, the report name, the version, and who
prepared it when. That block is what prints: `@media print` in `app.css`
hides the sidebar, top bar, page header, and toasts, drops the sticky
header, and lets the tables flow across pages. The Print button calls the
browser's `window.print`.

---

## 4. Small things worth knowing

- `Labels.Category` (Application) gives "Supplies and materials" from
  `SuppliesAndMaterials` for reports, exports, and the portal alike; the
  portal's private copy from Phase 5 was replaced with it.
- `ReportIndex` defaults its picker to the open version (the one being
  worked on), else the newest.
- The department filter on the detail report lives in the query string
  (`?department=`), so a bookmarked or printed report is reproducible and
  the XLSX link carries the same filter.
- Ten money columns fit a 1440 px laptop because the report table wraps its
  headers and tightens its padding (`.cb-report-table`); the name column
  keeps a minimum width so wrapping happens in the headers, not the names.

---

## 5. Things to read, in order

1. `Application/Import/ImportAnalyzer.cs` and `ImportAnalyzerTests.cs` (the rules, pure)
2. `Application/Import/BudgetImportService.cs` (preview, commit, audit event)
3. `Components/Admin/Import/BudgetImport.razor` (InputFile, preview grid, confirm)
4. `Application/Reports/ReportBuilder.cs` and `ReportTables.cs`
5. `Components/Admin/AdminExportEndpoints.cs` and the `@media print` block in `app.css`
