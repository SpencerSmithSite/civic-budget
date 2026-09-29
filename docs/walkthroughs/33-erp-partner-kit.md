# Walkthrough 33: The ERP partner kit

Before this phase, connecting a real ERP to CivicBudget meant writing an adapter inside CivicBudget
for that ERP. The contracts were clean (an adapter fills an `ErpChart` or an `ErpActuals` and
nothing else changes), but a vendor still needed me. This phase turns the four exchanges into a
published API with a client that already works, so an ERP vendor connects by building four
endpoints on their side.

The decisions are in ADR-0045. The kit itself is [`docs/partners`](../partners/README.md).

## 1. The four exchanges, all with an API

| Exchange | Contract | File | API (new or existing) |
|---|---|---|---|
| Chart of accounts | `ErpChart` | `ErpChartFileSource` | `IErpChartApi` (new) |
| Actuals | `ErpActuals` | `ErpActualsFileSource` | `IErpActualsApi` |
| Employees | `ErpEmployees` | `ErpEmployeeFileSource` | `IErpEmployeesApi` |
| Budget journal | `ErpBudgetJournal` | `BudgetJournalFile` | `IErpBudgetApi` |

The chart was the only exchange with no API, so it gained one. `ChartSyncService` now reads a chart
from either source into the same preview and commit (`PreviewFromErpAsync`, `CommitFromErpAsync`),
and the Chart sync page has a Fetch card beside the upload, like the actuals page. The simulated
ERP gained `SimulatedErpChartApi`, which holds the seeded chart, so in the demo a fetch finds
nothing to change until someone edits the chart.

## 2. The published API

[`openapi.json`](../partners/openapi.json) describes four endpoints under
`/v1/entities/{entityId}`. I wrote it by hand, because the descriptions are the part a vendor reads,
and held it to the code with a test (section 5).

The JSON shapes are their own records in `Infrastructure/Erp/Http/ErpApiContract.cs` (`ApiChart`,
`ApiActuals`, and so on), with `ErpApiContract.ToContract` and `FromContract` to translate. It
would be less code to serialize `ErpChart` directly, but then renaming a C# property would change
the public API without anyone noticing.

`ErpApiContract.Json` holds the rules both sides use:
- **camelCase names, enumerations by name.** `allowIntegerValues: false` refuses `"category": 2`,
  since a number means nothing to a reader of the file.
- **Required means required.** `RespectRequiredConstructorParameters` refuses an answer with a
  required field missing, and `RespectNullableAnnotations` refuses a null where the record does
  not allow one. Without them, a missing amount reads as zero, and a budget quietly built on a
  zero is the worst outcome.
- **Extra fields are ignored,** so a vendor can add to their answers.
- **Nulls are not written,** so the samples show only what is there.

Optional fields have default values in the records (`string? DepartmentCode = null`), which is how
`RespectRequiredConstructorParameters` tells optional from required.

## 3. One adapter for every ERP

`HttpErpAdapter` implements all four contracts over HTTP. The reads (chart, actuals, employees)
turn every failure into a sentence for the Fiscal Officer:

| What happened | What the page says |
|---|---|
| 401 or 403 | The ERP did not accept CivicBudget's key; ask the operator to check the connection |
| 404 with a problem body | The ERP's own words ("The ERP has no books for FY2031") |
| Connection refused | The ERP could not be reached; try again or upload a file |
| Timeout | The ERP did not answer within 60 seconds |
| A required field missing | The answer is not in the published form, and which field |
| Actuals for a different year | Refused, rather than filed under the year asked for |

The journal post follows ADR-0035's rule that no answer means no knowledge. A clear refusal (a 422
listing the accounts, or a 400, 401, 403, or 404) comes back as a refusal, and nothing posted. A
lost answer or a 5xx throws, and the send service keeps the send open for a retry with the same
id. The id travels in the body and in an `Idempotency-Key` header, so the ERP can recognize a
repeat.

## 4. Connections from the operator

Each government's connection is three settings under its id:

```
Erp:Connections:{government id}:BaseUrl
Erp:Connections:{government id}:ApiKey
Erp:Connections:{government id}:EntityId   (optional)
```

- **Where they live.** They come from configuration: user-secrets on a laptop, Secrets Manager or
  Key Vault in the cloud. An ERP key opens a government's books and payroll, and keeping it out of
  the database keeps it out of backups, exports, and anything that can read a table.
- **Why the id, not the slug.** An Administrator can change a government's slug; its id never
  changes.
- **Checked at startup.** `ErpConnectionsOptionsValidator` stops the app on a connection that is
  not HTTPS (plain HTTP is allowed to localhost only), has no key, or is filed under something
  that is not a government id. Failing at startup beats failing later on a Fetch button.

One adapter serves every government, but only some are connected. Each ERP contract gained
`IsConnected(governmentId)`, and each service's `Api` property returns the adapter only when the
signed-in government is connected. A government without a connection sees the file upload alone,
exactly as before.

`Program.cs` registers the HTTP adapter when any connection is configured
(`AddErpConnections`), and the simulated ERP otherwise, where demo data is seeded
(`AddSimulatedErp`).

## 5. Keeping the document honest

`ErpApiContractTests` fails when the document and the code disagree:
- every schema has exactly its record's properties, and the same required fields;
- a field that may be null says so in its type, and one that may not does not;
- every enumeration lists exactly the C# enum's names;
- the document has no schema the code lacks, and every `$ref` resolves;
- every JSON sample reads under the published rules, refusing fields the API does not define;
- every CSV sample reads through the same file source as a real upload;
- the journal CSV sample is byte for byte what `CsvWriter` writes.

I checked that the test bites by breaking the document four ways (a field removed, a required list
changed, a nullable type made plain, an enum value dropped); each failed.

## 6. The reference ERP

[`samples/CivicBudget.ReferenceErp`](../../samples/CivicBudget.ReferenceErp) is a small minimal-API
app that serves the four endpoints over the demo data, using the simulated ERP for the behavior. It
checks the Bearer key in constant time, answers unknown entities with a 404 problem, requires the
idempotency header to match the journal's id, and answers a refused journal with 422.

`HttpErpAdapterTests` start it on a free port and run the adapter against it over real HTTP. The
strongest test fetches all three reads through HTTP and compares them, as JSON, with what the
simulated ERP returns directly: the round trip through the wire records loses nothing.
`ErpConnectionTests` goes one level up: the chart sync service, the HTTP adapter, the reference
ERP, and a seeded database, with Maple Ridge connected and Pine Hollow not.

## 7. The guide

[`docs/partners/README.md`](../partners/README.md) is written for an ERP vendor who has never heard
of CivicBudget. It covers:
- the four exchanges and who starts each;
- files or API;
- setting up a connection;
- the rules behind the fields (send the whole chart; employee ids never change; journals are
  changes, whole or nothing, and a retry must not post twice);
- security;
- how to test against the reference ERP;
- versioning.

[`file-layouts.md`](../partners/file-layouts.md) documents the same four exchanges as files.
