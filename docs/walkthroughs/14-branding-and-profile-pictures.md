# Walkthrough 14 — Branding and profile pictures

Phase 10, a small one after the v1.1 phases: the product gets a mark, the
sidebar becomes the government's, and people get a face.

## 1. The mark

`Components/Common/Logo.razor` is an inline SVG: a civic building (pediment,
three columns, base) in white on a teal tile with rounded corners. Inline
rather than an `<img>` so it needs no request, scales crisply from 16 px to
the sign-in page, and carries `role="img"` with an accessible label. The
gradient id is suffixed per instance because several marks can share a
page. `wwwroot/favicon.svg` is the same drawing.

The admin sidebar shows the mark and the government's name in two bold
lines (`.cb-brand-gov`), no product name: it is the fiscal officer's
system, and the tenant is what they need to see. The public header and the
sign-in page keep "CivicBudget" beside the mark because they are not inside
any government yet. The collapsed rail keeps only the mark.

## 2. Profile pictures

The pieces, in the order a request passes through them:

1. **Upload** (`Components/Admin/ProfilePicture.razor`, `/admin/profile-picture`,
   reachable from the account menu and the account pages). An `InputFile`;
   on change the page calls `RequestImageFileAsync("image/png", 256, 256)`,
   which makes the browser decode the file and draw it at 256 px, then reads
   the result into memory and hands the bytes to the service. Anything that
   is not an image fails there, in the browser, with a friendly message.
2. **Store** (`Infrastructure/Identity/UserAvatarService.cs`). Type and size
   are checked (`UserAvatar.MaxBytes`); the bytes go to `UserAvatars`, one row
   per user, and `ApplicationUser.AvatarUpdatedAtUtc` is set. That timestamp
   is the version: lists read it from the user row and never touch the bytes.
3. **Serve** (`IdentityEndpoints.cs`, `GET /Account/Avatar/{userId}`).
   Requires authentication; the service's read joins to `Users` on the
   current government, so a picture is visible only inside the tenant. The
   response is cached privately for a year: the URL includes the version, so
   a new upload is a new URL.
4. **Show** (`Components/Common/Avatar.razor`). One component decides
   picture or initials. Callers that already know the version (the user
   list's `UserSummaryDto.AvatarVersion`) pass it; the top bar and the
   timelines ask `IUserAvatarService.GetVersionAsync`, cached per circuit,
   and subscribe to `Changed` so an upload updates the top bar in place.
   `Display.Initials` replaced four private copies of the same helper.

Administrators can remove a user's picture from the edit page (the
moderation need); nobody uploads for someone else.

## 3. Quieter audit trail

The department round (Phase 9d) records "Police submitted its budget
request" as a named event and, through the interceptor, also recorded the
submitted-by id and timestamps as field changes. `[NotAudited]` on those
properties tells `AuditInterceptor.IsOptedOut` to skip them; Status,
Narrative, and the return note are still audited field by field, and
`DepartmentRequestServiceTests` asserts both halves.

## 4. Things to read

1. `Components/Common/Logo.razor` and the `.cb-brand-gov` rule in `app.css`
2. `Components/Admin/ProfilePicture.razor` (the browser-side resize)
3. `Infrastructure/Identity/UserAvatarService.cs` and the endpoint in `IdentityEndpoints.cs`
4. `Components/Common/Avatar.razor`
5. `tests/CivicBudget.IntegrationTests/UserAvatarServiceTests.cs`, ADR-0028
