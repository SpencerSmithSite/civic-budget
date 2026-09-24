# Walkthrough 10: Ohio account numbers

Phase 9a, the first step of v1.1. Read `docs/research/ohio-account-numbers.md`
first for what the numbers mean; this is how the code carries them.

## 1. One value object, three stored codes

`Domain/Accounts/AccountNumberFormat.cs` is the per-government layout: the
widths of the fund, middle, and object segments, the separator, and whether
the middle segment is called Program (UAN) or Department (most county ERPs).
`Government` owns one (`OwnsOne` in `GovernmentConfiguration`, five columns
on the government's row) and defaults to the UAN village layout.

`Domain/Accounts/AccountNumber.cs` has the two functions. `Compose` writes
`1000-725-121`, or `1000-110` for a line with no department, padding numeric
codes to the width. `TryParse` is deliberately lenient about separators
(`-`, `.`, space, `/`, or none) and strict about content (the fund segment
must be digits, no segment may contain punctuation), so a search box can try
it first and fall back to matching names. `AccountNumberTests` covers both.

Why compose rather than store? The three codes are what every rule uses: an
expenditure line needs a department, revenue sits at fund level, a
department user may edit only their department. Storing the full number as
well would duplicate them and drift on a rename. Snapshots are the one
place the composed number is stored, because a snapshot is a record of
what was published.

## 2. Where it shows up

- `BudgetLineDto.AccountNumber` and `BudgetWorkspaceDto.NumberFormat`:
  `BudgetEntryService` composes the number while mapping, using the
  government it already loads. The account grid and the department view
  show the number in place of the bare object code; the grid's search
  matches numbers with separators ignored on both sides; the department
  filter and the add-line form use the government's word for the segment.
- Reports: `DetailLineDto.AccountNumber`; the department detail report and
  its XLSX show it.
- Exports: the workspace export writes `Account Number` first, then the
  codes and name, then the figures.
- Import: `ImportFileParser.Parse(file, format)` accepts an `Account Number`
  column (a full number in the row wins over the code columns; one that
  does not parse is left for the analyser to report as an unknown fund).
- Portal: `PublishedBudgetSnapshotLine.AccountNumber`, frozen at publish
  and backfilled by `AddAccountNumberFormat` for older snapshots; shown on
  department pages, in the download, and searchable (`1000-110` or
  `1000110`).
- Settings: Government settings has the format fields with a live example.

## 3. The seed

Department codes were letters (`PD`, `ST`), which made `1000-PD-5120`.
They are now UAN program numbers: 110 Police, 310 Parks & Recreation, 410
Building & Zoning, 530 Water, 540 Sewer, 620 Streets & Service, 715 Council
& Mayor, 725 Finance; Pine Hollow has 710 Trustees and 610 Road and a
dotted "Department" format so the two tenants show the setting at work.
Reseed to see it: `docker compose down -v && docker compose up -d`.

## 4. Things to read

1. `Domain/Accounts/AccountNumber.cs` and `AccountNumberTests.cs`
2. `Application/Import/ImportFileParser.cs` (the `Account Number` column)
3. `Infrastructure/Persistence/Migrations/*_AddAccountNumberFormat.cs` (defaults and the backfill)
4. ADR-0024
