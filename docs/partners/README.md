# Connecting an ERP to CivicBudget

This guide is for ERP vendors: the companies whose financial systems Ohio villages, townships, and
cities keep their books in. CivicBudget is where those governments build their budget. It needs
four things to pass between the two systems, and this kit describes each one precisely enough to
build without talking to me first (though I would like to hear from you:
CivicBudget@spencersmith.site).

What is in the kit:

| File | What it is |
|---|---|
| This guide | The four exchanges, how a connection is set up, and the rules that matter |
| [`openapi.json`](openapi.json) | The API in OpenAPI 3.1, the part you build |
| [`file-layouts.md`](file-layouts.md) | The same four exchanges as import and export files, for ERPs without an API |
| [`samples/`](samples) | An example of every request, answer, and file |
| [`samples/CivicBudget.ReferenceErp`](../../samples/CivicBudget.ReferenceErp) | A small working ERP that implements the API, to read and to test against |

## The four exchanges

| Exchange | Direction | API | File | Who starts it |
|---|---|---|---|---|
| Chart of accounts | ERP to CivicBudget | `GET /v1/entities/{entityId}/chart` | Chart export | The Fiscal Officer, when the chart changes |
| Actuals | ERP to CivicBudget | `GET /v1/entities/{entityId}/actuals/{fiscalYear}` | Actuals export | The Fiscal Officer, usually after each month closes |
| Employees | ERP to CivicBudget | `GET /v1/entities/{entityId}/employees` | Employee export | The Fiscal Officer, while building the personnel budget |
| Budget journal | CivicBudget to ERP | `POST /v1/entities/{entityId}/budget-journals` | Journal import file | The Fiscal Officer, after council adopts a budget or an amendment |

CivicBudget always calls the ERP; the ERP never calls CivicBudget. A person starts every exchange,
sees a preview of what it would change, and confirms it. Nothing runs on a timer.

Some terms, for readers outside Ohio government:

- **Fund:** a separate set of books for money that may only be spent on certain things. The
  General Fund pays for most services; the Street fund's gasoline tax may only be spent on streets.
- **Appropriation:** the spending limit council adopts for each account. The budget journal
  carries appropriations and revenue estimates into the ERP.
- **Encumbrance:** money committed by a purchase order and not yet spent.
- **Account number:** fund, then department (called a program in the state's Uniform Accounting
  Network chart), then object code (what the money is for), such as `1000-110-5110`: General Fund,
  Police, Salaries. Revenue accounts have no department: `1000-4110`.

## Two ways to connect

**By file**, today, with no work on your side if your ERP can export a CSV or Excel file. The Fiscal
Officer downloads an export from your ERP and uploads it on the matching CivicBudget page, and
downloads the journal file from CivicBudget and imports it into your ERP. The layouts are in
[`file-layouts.md`](file-layouts.md). They are one row per item with plain column names, so most
ERPs' report writers can produce them.

**By API**, which removes the downloading and uploading. You build four endpoints to the
[OpenAPI description](openapi.json). CivicBudget already has the client side: an adapter that talks
to any ERP implementing this API (`HttpErpAdapter`). No code is added to CivicBudget for your ERP.
The Fiscal Officer gets a **Fetch** button beside each upload, and the **Send to ERP** page can
post the journal directly, beside the file download.

A government can use both: files for one exchange while the API for another is being built.

## Setting up an API connection

1. **Build the four endpoints.** Start from [`openapi.json`](openapi.json) and the samples. The
   reference ERP shows every endpoint working.
2. **Issue a key for each government.** The key should open that government's data and no other's.
   CivicBudget sends it as a Bearer token, only over HTTPS.
3. **Give the operator the address, the key, and your id for the government.** The operator (whoever
   runs CivicBudget for the government) stores them in CivicBudget's secret store, never in its
   database or on a page, under the government's CivicBudget id:

   ```bash
   dotnet user-secrets set "Erp:Connections:<government id>:BaseUrl" "https://erp.example.com/civicbudget" --project src/CivicBudget.Web
   dotnet user-secrets set "Erp:Connections:<government id>:ApiKey" "<the key>" --project src/CivicBudget.Web
   dotnet user-secrets set "Erp:Connections:<government id>:EntityId" "<your id for the government>" --project src/CivicBudget.Web
   ```

   In the cloud the same settings come from AWS Secrets Manager or Azure Key Vault, as environment
   variables named `Erp__Connections__<government id>__BaseUrl` and so on. `EntityId` is optional;
   without it CivicBudget sends the government's portal name (`maple-ridge-oh`). CivicBudget checks
   every connection when it starts and will not start with an address that is not HTTPS or a
   missing key.
4. **Restart CivicBudget.** The connected governments see Fetch and Send buttons; the others keep
   the file uploads.

## The rules that matter

The OpenAPI file has every field. These are the rules behind them, the ones a vendor would
otherwise learn from a support call.

### Every exchange

- **One entity per request.** `entityId` in the path is your id for one government.
- **JSON with camelCase names; enumerations by name** (`"SpecialRevenue"`, not `2`). Amounts are
  JSON numbers in dollars and cents. Dates are `yyyy-MM-dd`.
- **Every field the OpenAPI file lists as required must be there.** CivicBudget refuses an answer
  with a required field missing rather than reading it as zero, because a budget built on a zero
  that should have been a number is worse than an error.
- **Extra fields are fine.** CivicBudget ignores fields it does not know, so you can add to your
  answers without breaking anything.
- **Errors use the problem format** (RFC 9457): `{"title": "...", "detail": "..."}`. CivicBudget
  shows the detail to the Fiscal Officer, so write it for them: "The ERP has no books for FY2031"
  helps; "Entity lookup failed" does not.
- **Status codes:** 401 or 403 for a bad key (CivicBudget tells the user to have the operator check
  the connection), 404 for an unknown entity or a year with no books, 400 for a request you cannot
  read.

### Chart of accounts

- Send the **whole chart every time**, not what changed. CivicBudget compares it with its own: new
  codes are added, renamed or recategorized ones updated, and codes you no longer list are
  deactivated.
- **Send retired codes with `active: false`** rather than leaving them out. CivicBudget never
  deletes a code, because last year's budget still uses it, but a code you mark inactive keeps its
  name in history.
- Every object code carries a **type** (revenue, expenditure, transfer in, transfer out) and a
  **reporting category** (personal services, taxes, and so on). CivicBudget's rules depend on
  them: which accounts count toward a department's total, which lines the certificate of estimated
  resources adds up. CivicBudget refuses a chart that would change an object's type while budget
  lines use it.
- `numberFormat` is optional. When you send it, CivicBudget writes account numbers your way.

### Actuals

- One **fiscal year** per request, named by the year it ends in: FY2026 for a July 2025 to June
  2026 year.
- Send the **whole year through the last closed month** each time. CivicBudget replaces what it
  holds for that year with your answer. `throughPeriod` 12 means the year is closed, and only a
  closed year fills the prior-year actual column of next year's budget.
- **Periods are fiscal months**, 1 to 12. In a July-to-June year, July is 1.
- **Amounts are as your books show them:** receipts on revenue accounts and spending on expenditure
  accounts, both normally positive. A refund can make a month negative; that is fine.
- **Cash** is each fund's balance at the end of `throughPeriod`.

### Employees

- `employeeId` **must not change.** It is how CivicBudget finds the same person's position next
  year and keeps what the budget owns (a planned raise, months budgeted) while refreshing what you
  own (pay, title, plans, funds).
- **Plans are named the way the government names them** in CivicBudget's personnel settings (for
  example `OPERS`, `OP&F police`, `Medical (PPO)`). If your ERP has its own plan codes, translate
  them in your endpoint.
- **Fund shares add up to 100.**
- Send everyone currently employed. A budgeted employee you leave out leaves their position vacant
  rather than removed, because the department usually means to fill it.
- A department, fund, or plan name CivicBudget does not recognize refuses the whole sync until it
  is fixed, since a half-applied roster would misstate every personnel line. The preview names each
  one.

### Budget journal

- **Amounts are changes, not totals.** An original budget arrives as every line's amount. An
  amendment arrives as only the lines that moved, and a decrease is negative.
- **Post the journal whole or not at all.** If any account is refused, post nothing and answer 422
  with each refused account and its reason. The Fiscal Officer fixes the chart and sends again.
- **Recognize a retry.** When CivicBudget gets no answer (a timeout, a dropped connection, a 5xx),
  it cannot know whether the journal posted, so the send stays open and can be sent again. A retry
  carries the same `externalId`, also in the `Idempotency-Key` header. Answer a repeat with the
  first answer and post nothing new. This is the one rule that, if missed, puts money in the
  books twice.
- `description` is at most 100 characters, one for the whole journal ("FY2027 Original, resolution
  2026-50"). `date` is the posting date.
- Answer 200 with `posted: true` and your `journalNumber`. CivicBudget records the number beside the
  send, so the government's auditor can follow a budget into the ERP.

## Security

- **HTTPS only.** CivicBudget refuses to start with a connection that is not HTTPS (plain HTTP is
  allowed only to localhost, for testing against the reference ERP).
- **A key per government,** sent as `Authorization: Bearer <key>`. Compare it in constant time on
  your side, and let it open one entity only.
- **CivicBudget keeps keys in the secret store only,** never in its database, a page, or a log. It
  logs each call's method, path, and status, never a body: the bodies are the government's books
  and payroll.
- **Rotating a key:** issue the new one, have the operator update the secret and restart
  CivicBudget, then retire the old one.
- **Who did what:** every sync and send is in CivicBudget's audit trail with the user and time, and
  every export that leaves CivicBudget is in its security log.

## Testing your implementation

1. **Read the reference ERP.** It is about 120 lines
   ([`ReferenceErpServer.cs`](../../samples/CivicBudget.ReferenceErp/ReferenceErpServer.cs)) and
   implements every rule above over fictional data:

   ```bash
   dotnet run --project samples/CivicBudget.ReferenceErp -- --urls http://localhost:5090 --ReferenceErp:ApiKey=local-test-key
   ```
   ```bash
   curl -H "Authorization: Bearer local-test-key" http://localhost:5090/v1/entities/maple-ridge-oh/chart
   ```
2. **Compare your answers with the samples.** Each sample in [`samples/`](samples) is checked by
   CivicBudget's tests to contain only published fields and to read cleanly.
3. **Point a local CivicBudget at your server.** Configure a connection for Maple Ridge (step 3
   above), sign in as its Fiscal Officer, and use Fetch on the Chart sync, Actuals sync, and Employees from
   the ERP pages, and send an adopted budget from its Send to ERP page.

CivicBudget's own tests run the adapter against the reference ERP over real HTTP
(`HttpErpAdapterTests`), and hold `openapi.json` to the code field by field
(`ErpApiContractTests`), so the document and the adapter cannot drift apart.

## Versioning

The path carries the version (`/v1`). Within v1, CivicBudget may send or accept new optional
fields, and you may add fields to your answers. Anything else (a field removed or renamed, a new
enumeration value, a changed rule) is v2, and CivicBudget would support both while vendors move.

## Not in the API yet

- CivicBudget does not send the chart back to the ERP; the ERP owns the chart.
- No push from the ERP (webhooks). The Fiscal Officer fetches when they need to.
- No purchase orders or vendor detail, only encumbrance totals by account.

If your customers need one of these, tell me.
