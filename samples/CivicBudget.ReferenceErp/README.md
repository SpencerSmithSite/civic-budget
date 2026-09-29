# Reference ERP

A small ERP that implements CivicBudget's ERP API ([`docs/partners/openapi.json`](../../docs/partners/openapi.json))
over the demo governments' fictional data. It is for ERP vendors reading how the four endpoints
behave, and for CivicBudget's tests, which run the HTTP adapter against it.

```bash
dotnet run --project samples/CivicBudget.ReferenceErp -- --urls http://localhost:5090 --ReferenceErp:ApiKey=local-test-key
```

It hosts two entities, `maple-ridge-oh` and `pine-hollow-twp-oh`:

```bash
curl -H "Authorization: Bearer local-test-key" http://localhost:5090/v1/entities/maple-ridge-oh/chart
curl -H "Authorization: Bearer local-test-key" http://localhost:5090/v1/entities/maple-ridge-oh/actuals/2025
curl -H "Authorization: Bearer local-test-key" http://localhost:5090/v1/entities/maple-ridge-oh/employees
```

To connect a local CivicBudget to it, set a connection for Maple Ridge (its id is on the
database's `Governments` table after a reseed) and restart:

```bash
dotnet user-secrets set "Erp:Connections:<Maple Ridge's id>:BaseUrl" "http://localhost:5090" --project src/CivicBudget.Web
dotnet user-secrets set "Erp:Connections:<Maple Ridge's id>:ApiKey" "local-test-key" --project src/CivicBudget.Web
```

A connection replaces the simulated ERP, so Pine Hollow then has no API and offers file uploads
alone. Remove the two settings to go back.

It is a reference, not a product: one key opens every entity, and posted journals live in memory
until it stops. The integration guide is [`docs/partners/README.md`](../../docs/partners/README.md).
