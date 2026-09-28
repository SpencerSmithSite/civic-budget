# Walkthrough 27: Email, two-step sign-in, and onboarding

Before a buyer asks about budgets, they ask about three things:

- Does it email people?
- Can we require MFA?
- How does a new customer get set up without a developer?

Phase 28 answers all three. It uses no Microsoft sign-in, and it works on the free demo without
sending a single real email.

## 1. Email: the transactional outbox

Every email is a row, `OutboxEmail`, written **in the same save** as the change that caused it.
When a department submits, `DepartmentRequestService` records the submission, the audit event, and
one email per Administrator and Fiscal Officer, then calls `TrySaveAsync` once.

- There is never a submission without its notice, or a notice about a submission that did not save.
- A mail server that is down does not fail the submission. The email waits.
- The rows are the record of what was sent to whom, which an auditor asks for.

What happens next depends on `EmailOptions.Mode`:

| Mode | When | What happens |
|---|---|---|
| Outbox (default) | the demo, and any deployment without a mail server | The email is **held**. An Administrator reads it on **Email outbox**, and its links work from there. |
| Smtp | a mail server is configured | `EmailDeliveryService` sends it through MailKit; any provider that speaks SMTP works. |

The sender does not poll. A timer checking the outbox every minute would keep the demo's
serverless database awake forever (walkthrough 18). Instead, after a successful save the service
calls `IEmailOutbox.Notify()`, a write to an in-memory channel that costs nothing, and the sender
wakes and sends everything pending. A refused email is retried after a minute per attempt so far.
After five attempts it is marked failed, with the server's reason on the outbox page.

Links in emails need an absolute address. A page knows it; a service writing an email does not.
`IAppLinks` asks Blazor's `NavigationManager` for the address the page is served from, unless
`App:PublicUrl` fixes it. Behind Azure's load balancer the request arrives as http, so the app now
honours `X-Forwarded-Proto` and the links say https.

## 2. The emails

| Email | To | Carries |
|---|---|---|
| A department submitted | Administrators and Fiscal Officers, not the person who submitted | who, which budget, a link to the department |
| A request was returned | the department's users | the note, a link |
| Forgot your password? | the address typed, if it has an account | a reset link |
| Welcome | a new user | a link to choose their password |

**No password is ever emailed.** A reset link and a welcome link are Identity's password reset
token: single use, because it is tied to the security stamp, which changes when the password does,
and valid for a day. The forgot-password page says "if this address has an account" whether it
does or not, so it cannot be used to learn who works for the village.

Creating a user now defaults to emailing them a link: nobody but the user ever knows the password.
The temporary password an administrator types is still there, for someone without email.

## 3. Two-step sign-in

After the password, sign-in asks for a six-digit code from an authenticator app. Identity has the
pieces: the authenticator key, time-based codes, recovery codes, and "remember this browser". The
phase adds the pages.

- **Set up** (Your account, Two-step sign-in):
  1. Scan the QR code.
  2. Type the code the app shows.
  3. Save the ten recovery codes, shown once.

  The QR code is drawn as SVG on the server (QRCoder), so the sign-in pages load no third-party
  script.
- **Sign in**: password, then code (with "don't ask again on this computer for 14 days"), or a
  recovery code if the phone is lost.
- **Require it**: an Administrator turns it on under Government settings. Everyone without it is
  signed out. At their next sign-in the claims factory adds a `mfa_setup` claim, and
  `RequireMfaMiddleware` keeps them on the setup page until they finish. This is the same pattern
  as the temporary-password rule.
- **Reset it**: for a user who lost their phone and their recovery codes, the Administrator resets
  it on the user's page. Their old key stops working, and they set up the new phone.

Every change is an event on the audit trail: turned on, turned off, new recovery codes, a recovery
code used, required, and reset.

## 4. Setting up a new government

Creating a government is the vendor's job, not a tenant's, so no page a government's users can
reach does it. It is a command, like the nightly `--reseed`:

```bash
dotnet run --project src/CivicBudget.Web -- --provision --name "Village of Cedar Falls" --type Village --slug cedar-falls-oh --admin-name "Jordan Ellis" --admin-email jordan@cedarfalls.example
```

It creates the government and its first Administrator, with no password, and emails a link to
choose one. With no mail server it prints the link for the operator to pass on.

The new Administrator signs in to an Overview that says **Finish setting up**, pointing to
**Getting started**. The checklist is worked out from what is there, not from ticks:

| Step | Done when |
|---|---|
| Your government | always (review the settings) |
| Chart of accounts | there are funds, departments, and revenue and expenditure accounts (from the ERP or its file) |
| A fiscal year | there is one |
| The first budget | there is a budget version |
| Your team | there is someone besides the Administrator |
| Personnel, actuals, two-step sign-in, the portal | optional |

A running government sees every required step done and the optional pieces it has not used.

## 5. Tests

- **Domain** (`OutboxEmailTests`): held versus pending, sent once, retries up to the limit.
- **Application** (`EmailTemplateTests`): each email says why and where to go, and none carries a
  password.
- **Integration** (`AccountAndOnboardingTests`):
  - a submission notifies the fiscal authority, and a return notifies the department;
  - a reset link works once, and an unknown address gets nothing;
  - a new user chooses their password from the welcome link;
  - only Administrators read the outbox;
  - with a mail server, a good address is sent and a bad one is retried then failed;
  - requiring two-step sign-in flags users until they set it up, and a reset flags them again;
  - a new government is provisioned, the slug is unique, and its checklist starts at 1 of 5.
- **bUnit**:
  - the outbox's links are clickable but an email cannot inject markup;
  - the checklist sends Administrator steps to an Administrator;
  - the new-user form defaults to the emailed link;
  - `RequireMfaMiddleware` holds a flagged user on setup.
- **In a browser**:
  - forgot password, then the outbox, then the reset link;
  - two-step setup with a real code, sign-in with a code and with a recovery code;
  - require it, and the Fiscal Officer lands on setup;
  - `--provision`, the link, and the new Administrator's first sign-in.
