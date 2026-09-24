# Walkthrough 02: Identity, authorization, and the admin area

Phase 2 added sign-in, roles, permissions, and the setup screens. The role names
here are the ones the app launched with; in Phase 9c they became Administrator,
Fiscal Officer, Department User, and Viewer on screen (walkthrough 12), while the
stored names stayed the same.

---

## 1. Three kinds of authorization, and where each one lives

| Kind | Question it answers | Where | Example |
|---|---|---|---|
| **Role** | Is this user the Fiscal Officer? | Identity role claims | `Roles.FinanceDirector` |
| **Policy** | May this user *publish*? | `Web/Security/AuthorizationPolicies.cs` maps names to roles | `[Authorize(Policy = Policies.CanPublish)]` |
| **Resource-based** | May this user edit *this* line? | `BudgetLineEditHandler` + `BudgetLinePermissions` | `AuthorizeAsync(user, new BudgetLineResource(status, deptId), Policies.CanEditBudgetLine)` |

Pages never mention role names. They name a **policy**, and one file decides
what the policy means. When "who may import" changes, one line changes and
`AuthorizationPolicyTests` still proves the whole matrix.

The resource-based rule is written **once** as a pure function,
`Application/Security/BudgetLinePermissions.CanEdit(user, versionStatus, departmentId)`,
and adapted to ASP.NET Core by the handler in `Web/Security/BudgetLineEditRequirement.cs`.
Services will call the function directly in Phase 3; components call the
policy through `IAuthorizationService`; both get the same answer. Unit tests
(`BudgetLinePermissionsTests`) cover the function; policy tests cover the
adapter.

*Interview question:* "Why not just check the role in the component?"
Because the department user's rule depends on the line's department and the
version's status. Roles can't express that. And a rule in a component can't
protect an import or an API.

---

## 2. Where the signed-in user comes from in each render mode

This is the subtle part of Blazor Web Apps, and the one thing that broke
during development.

```
HTTP request (static SSR page, login POST, health check)
   UseAuthentication  →  HttpContext.User
   CurrentUserMiddleware  →  copies it into the request scope's CurrentUserContext

Interactive Server circuit (admin pages)
   SignalR connection opens  →  NEW DI scope for the circuit
   CurrentUserCircuitHandler.OnCircuitOpenedAsync  →  reads AuthenticationStateProvider
                                                   →  fills the circuit scope's CurrentUserContext
```

`CurrentUserContext` (`Infrastructure/Security/`) is scoped and implements
both `ICurrentUser` and `ITenantContext`. The tenant query filter from
Phase 1 reads `ITenantContext.GovernmentId`, which now comes from the
`government_id` **claim** issued at sign-in. So:

sign-in → claims factory adds `government_id` → cookie → middleware or
circuit handler → `CurrentUserContext` → `DbContext` query filter.

Without the circuit handler, every DbContext created on a circuit would see
no tenant and return no rows: the admin overview would show 0 funds. The
"5 funds / 8 departments / 28 accounts" on the overview page is the proof
that the chain works (Pine Hollow's rows are not counted).

**Files:** `Web/Security/CurrentUserMiddleware.cs`,
`Web/Security/CurrentUserCircuitHandler.cs`,
`Infrastructure/Identity/ApplicationUserClaimsPrincipalFactory.cs`.

---

## 3. Identity lives outside the tenant filter, on purpose (ADR-0015)

`ApplicationUser` does **not** implement `ITenantOwned`. Sign-in has to find
the user by email *before* any tenant is known; a filtered `AspNetUsers`
table would make login impossible. Instead:

- `ApplicationUser.GovernmentId` records the tenant.
- `UserAdminService` scopes every query with an explicit
  `WHERE GovernmentId = @tenant` and refuses to load users of another
  government (`UserAdminServiceTests.Cannot_read_or_edit_a_user_from_another_government`).
- Department assignments (`UserDepartments`) are validated against the
  *filtered* `Departments` set, so an admin can't assign a department from
  another tenant (`Rejects_departments_that_belong_to_another_government`).

This is the one deliberate exception to "everything is filtered", and it is
documented as such because an interviewer who knows multi-tenancy will ask.

---

## 4. Application layer and EF Core (ADR-0014)

Phase 1's architecture note said Application defines interfaces that
Infrastructure implements. Phase 2 made that concrete with
`ICivicBudgetDbContext` (the domain `DbSet`s and `SaveChangesAsync`) and
`ICivicBudgetDbContextFactory`. The Application project references the
`Microsoft.EntityFrameworkCore` package for the LINQ surface (`Include`,
`ToListAsync`), but **not** the SQL Server provider, Identity, or ASP.NET
Core. `ArchitectureTests` pins this down.

Why not one repository per entity? Repositories over EF Core mostly
re-implement `DbSet`. The interface-over-DbContext approach (popularized by
Jason Taylor's Clean Architecture template) keeps queries expressive and
still lets tests substitute the factory. The trade-off: Application is
coupled to EF Core's LINQ shape. That is an acceptable coupling for a
project whose persistence story is "EF Core".

---

## 5. The Result type and how validation reaches the screen

`Application/Common/Result.cs`: a use case returns `Result` or `Result<T>`
with a list of `ValidationError(PropertyName, Message)`. Expected problems
(empty name, duplicate code, department from another tenant) are results;
programming errors and tenant violations still throw.

Flow for a form:

1. Component builds a request record and calls the service.
2. Service runs the FluentValidation validator
   (`ValidationExtensions.ValidateToResultAsync`), then its own checks
   (uniqueness), returning a `Result`.
3. Component calls `editContext.ApplyErrors(messageStore, result)`
   (`Web/Components/Common/EditContextResultExtensions.cs`), which pushes
   property errors into Blazor's `ValidationMessageStore` so
   `<ValidationMessage For>` renders them; form-level errors go to
   `<ResultAlert>`.

The component never references FluentValidation. The validator never
references Blazor. Screenshot-worthy proof: "Fund code 1000 is already in
use." appearing under the Code field.

---

## 6. Setup services: one shape, five times

`FundService`, `DepartmentService`, `AccountService`, `FiscalYearService`,
`GovernmentSettingsService` all follow: validate → load through the
filtered context → change through the entity's methods → save. Read
`FundService` once and the others are obvious. Two rules that needed the
database and therefore live in services rather than validators:

- Codes are unique **per government** (a friendly message; the unique index
  from Phase 1 is the guarantee).
- An account's **type** can't change once budget lines use it, because that
  would silently move money between resources and appropriations
  (`SetupServiceTests.Account_type_cannot_change_once_the_account_has_budget_lines`).

Reference data is never deleted, only deactivated (`IsActive`). Budget
history must keep pointing at the fund it was budgeted in.

---

## 7. Identity pages are static SSR; admin pages are Interactive Server

`Components/Account/Pages/_Imports.razor` carries
`[ExcludeFromInteractiveRouting]`. Login must set a cookie on an HTTP
response, and a circuit has no HTTP response. So the Account pages are plain
form posts (`method="post"`, `[SupplyParameterFromForm]`), while admin pages
are Interactive Server for grids and live validation. (In this phase each admin page
declared `@rendermode InteractiveServer`; that left the layout static, and the fix was
to set the mode once on `Routes` in `App.razor`, see ADR-0002.)
`RedirectToLogin` uses `forceLoad: true` for the same reason: a circuit
cannot navigate into a static page without a full request.

---

## 8. Things worth noticing in the code

- `IdentityRevalidatingAuthenticationStateProvider` re-checks the security
  stamp every 30 minutes on open circuits. `UserAdminService` rotates the
  stamp when a role changes or a user is locked, so open sessions lose
  access without waiting for the cookie to expire.
- `UserAdminService` refuses to let an admin lock their own account or drop
  their own Admin role: the classic "I locked myself out" support ticket,
  prevented.
- `dotnet new blazor -au Individual` was scaffolded into a scratch folder and
  only the pieces this app needs were ported: no registration, external
  logins, passkeys, or 2FA. Less surface, less to explain.
- `Display.Enum` turns `SuppliesAndMaterials` into "Supplies & Materials".
  Presentation only; the enum names are the contract.
- Namespace `Components.Common` rather than `Components.Shared`: `Shared` is a
  reserved word in VB.NET (analyzer CA1716), relevant to a shop with a VB.NET
  product.
