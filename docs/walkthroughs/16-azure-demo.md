# Walkthrough 16 — The live demo on Azure

Phase 13. The app is hosted where hiring managers can sign in and use it, at
no monthly cost, with the demo reset every night.

## 1. Why Azure

The app needs two things a free host must provide: a persistent process
(Blazor Server keeps a WebSocket per user) and SQL Server. Azure has both as
always-free offers, and nothing else does. The SQL free offer is a real SQL
Server, so the migrations and the EF Core provider run unchanged. See
ADR-0030 for the alternatives.

## 2. What is deployed

`infra/azure/main.bicep`, one resource group:

- **SQL**: a logical server and a serverless General Purpose database with
  `useFreeLimit: true` and `AutoPause` when the limit is hit. It pauses after
  an idle hour; the first connection wakes it in about a minute.
- **Container Apps**: an environment, the app (external ingress on 8080,
  0.25 vCPU / 0.5 GiB, 0 to 1 replica, startup probe with a generous
  threshold for migrations), and a scheduled job that runs the same image
  with `--reseed` at 08:00 UTC.
- **Log Analytics** with a 1 GB daily cap.

The app and the job share the same secrets (the composed SQL connection
string, the demo password) and the same settings:
`Database__MigrateOnStartup=true`, `Database__SeedDemoData=true`. Nothing in
`Program.cs` is Azure-specific; the same `DatabaseOptions` served the AWS
design.

## 3. The reset

`dotnet CivicBudget.Web.dll --reseed` builds the host for its configuration
and services, calls `DatabaseInitializer.ResetAsync`, and exits without
serving. The reset drops every foreign key, then every table, migrates from
nothing, and seeds. It keeps the database itself: on Azure the database is
the free-offer resource. `DatabaseResetTests` proves a changed line and a
renamed user are gone and the seed is back.

## 4. Deploying

`scripts/azure-setup.sh` runs once: resource group, template, generated SQL
and demo passwords, an app registration with a federated credential for
`repo:SpencerSmithSite/civic-budget:ref:refs/heads/main`, Contributor on the
resource group, and the three ids as repository secrets plus the
`AZURE_DEPLOY` variable.

`.github/workflows/deploy-azure.yml` then runs on every push to `main`: the
`image` job builds and pushes `ghcr.io/spencersmithsite/civic-budget:<sha>`
(and `:latest`) whether or not Azure exists, so the package can be made
public before setup; the `deploy` job, gated on `AZURE_DEPLOY`, signs in with
OIDC, updates the app and the reset job to the new tag, and waits for
`/health`. CI compiles and lints the Bicep in a `bicep-build` job.

## 5. Things to read

1. `infra/azure/main.bicep`
2. `scripts/azure-setup.sh`
3. `.github/workflows/deploy-azure.yml`
4. `DatabaseInitializer.ResetAsync` and the `--reseed` branch in `Program.cs`
5. `infra/azure/README.md` for day-two operations; ADR-0030
