# Walkthrough 11 — The chart comes from the ERP

Phase 9b. CivicBudget stops being where funds, departments, and objects are
*maintained* and becomes where they are *received*.

## 1. The contract

`Application/Erp/ErpChart.cs` is the whole agreement between CivicBudget
and any ERP: three lists (funds with a category, departments or programs,
objects with a type and reporting category) and optionally the account
number format. `IErpChartSource` is the adapter interface; the only
implementation today, `ErpChartFileSource`, reads an export file. When VIP's
real export or API is known, that is the one class to change or add.

The file layout is deliberately forgiving: one row per code with `Kind`,
`Code`, `Name`, `Type`, `Category`, `Description`, `Active`, any column
order, "Special Revenue" or "SpecialRevenue" both accepted, blank `Active`
meaning active. What it is strict about is what a code *needs*: a fund
needs a category, an object needs a type and a category valid for that type
(the same rule `Account`'s constructor enforces). Every bad row is reported
at once with its row number.

## 2. Preview, then apply

`ChartDiff.Compute` is a pure function: for each code it says Add, Update,
Deactivate, Reactivate, or Unchanged, with the before and after text so a
person can judge it. Codes match case-insensitively. A code the ERP no
longer lists is *deactivated*, never deleted: budget lines and published
snapshots still point at it, and history keeps its name.

`ChartSyncService.PreviewAsync` reads the file and diffs it against the
database. `CommitAsync` reads and diffs again (the preview is a courtesy;
the commit is the gate, exactly like the line import) and applies each
change through the entities' own methods (`Fund.Update`,
`Department.Deactivate`...), so the domain rules run and the audit
interceptor records every field change. Then one `ChartSync` log row with
the change list as JSON, one audit event on the government, and the
government's `ChartSource` becomes `Erp`.

The confirm dialog warns when a file would deactivate more than a quarter
of the chart. That is what a partial export looks like, and it is the one
mistake this feature can make expensive.

## 3. Who owns the chart

`Government.ChartSource` is `Local` until the first sync. Under `Erp`:

- `FundService`, `DepartmentService`, and `AccountService` refuse `Save`
  and `SetActive` through `ChartOwnership.RefuseIfErpManagedAsync`, with a
  message that says where changes come from now.
- The three setup lists load `IChartSyncService.StatusAsync()` and render
  `ChartSourceBanner` ("Managed by the ERP. Last synced ... by ...") instead
  of the New button and the row menus.
- An Administrator can switch back to `Local` on the sync page, for a
  government that has no feed; the switch is an audit event.

## 4. The page

`Components/Admin/Chart/ChartSync.razor`: upload, four KPIs, the change
grid (unchanged hidden by default), Apply behind a confirm dialog, and the
sync history with a drawer showing what each sync changed. The Fiscal
Officer and Administrators may sync (`CanMaintainSetup`); only an
Administrator sees the ownership switch.

## 5. Things to read

1. `Application/Erp/ChartDiff.cs` and `ChartDiffTests.cs`
2. `Application/Erp/ErpChartFileSource.cs` and `ErpChartFileSourceTests.cs`
3. `Application/Erp/ChartSyncService.cs` (`Apply` is where the entities do the work)
4. `ChartSyncServiceTests.cs` (the audit interceptor recording a rename made by a sync)
5. ADR-0025
