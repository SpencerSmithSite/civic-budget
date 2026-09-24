# Walkthrough 12: Users, logons, and permissions

Phase 9c. Three things a county administrator expects: complete access for
themselves, a fire chief who sees the fire department and nothing else,
and logons they create and reset without ever owning a user's real password.

## 1. Administrator as a superset

Before this phase an Administrator managed users and setup but could not
touch a budget line; the Fiscal Officer role held the fiscal powers. The
customer's expectation is the other way round: the administrator can do
everything. Two changes:

- `CurrentUserExtensions.IsFiscalAuthority()` (Administrator or Fiscal
  Officer) replaced every `IsInRole(Roles.FinanceDirector)` in the
  services: line edits, beginning balances, workflow, publishing, import,
  chart sync. One helper, so "who may adopt a budget" is answered in one
  line and tested once.
- The four fiscal policies in `AuthorizationPolicies` include
  `Roles.Admin`; `AuthorizationPolicyTests` is the matrix.

Role *values* in the database did not change. Display names did:
`Roles.DisplayName` now says Administrator, Fiscal Officer, Department
User, Viewer, and user-facing messages say the same.

## 2. Temporary passwords

`ApplicationUser.MustChangePassword` is set when an administrator creates
an account or resets its password. The chain:

1. `ApplicationUserClaimsPrincipalFactory` adds the `pwd_change` claim when
   the flag is set, so the cookie carries it.
2. `MustChangePasswordMiddleware` redirects any authenticated request that
   carries the claim to `/Account/Manage/ChangePassword?forced=true`,
   except account pages, sign-out, static assets, and the portal.
3. The change-password page shows why, clears the flag, and calls
   `RefreshSignInAsync`, which reissues the cookie without the claim, then
   sends the user to the app.

A password reset also rotates the security stamp, so any session the user
already had ends. A claim rather than a database check keeps the
middleware free of I/O; the flag is rare and short-lived.

## 3. Department assignment bounds every read

The workspace, reports, exports, and search already filtered by the
user's `department_id` claims. The gap was the audit trail: a department
user could open the overview and read other departments' amount changes.
`AuditQueryService` now limits a department user to entries about their
own departments' budget lines, both in recent activity and in a line's
history, and `PermissionsTests` proves it end to end (the Fiscal Officer
changes a Streets line and a Police line; the Police chief sees one).

## 4. User administration is audited

Identity's tables are not `[Audited]`, so `UserAdminService` writes named
events on the government's trail in the acting administrator's name:
created (with role and department count), updated, password reset, locked,
unlocked. `UserAdminServiceTests` checks the create event and the flag.

## 5. Things to read

1. `Application/Security/CurrentUserExtensions.cs` and where it is used
2. `Web/Security/MustChangePasswordMiddleware.cs` and its tests
3. `Infrastructure/Identity/UserAdminService.cs` (`AuditAsync`)
4. `tests/CivicBudget.IntegrationTests/PermissionsTests.cs`
5. ADR-0026 and the revised role matrix in SPEC §7
