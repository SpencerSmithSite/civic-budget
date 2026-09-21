# Azure: the free live demo

What runs where, and how to operate it. The design is ADR-0030; the walkthrough is
`docs/walkthroughs/16-azure-demo.md`.

| Resource | Why | Cost |
|---|---|---|
| Azure SQL Database, serverless General Purpose, `useFreeLimit` | A real SQL Server; the app's migrations run unchanged | Free: 100,000 vCore-seconds and 32 GB a month, then it pauses rather than bills |
| Container Apps environment + app (consumption plan, 0.25 vCPU / 0.5 GiB, 0 to 1 replica) | Runs the Docker image, terminates TLS, supports the Blazor WebSocket | Free grant of 180,000 vCPU-seconds a month; zero replicas cost nothing |
| Container Apps job `civicbudget-reset` (cron `0 8 * * *`) | Runs the same image with `--reseed`: drop every table, migrate, seed | Same grant, about a minute a night |
| Log Analytics workspace (1 GB/day cap) | Container Apps requires one | First 5 GB a month free |

## First time

```bash
brew install azure-cli gh
az login
LOCATION=centralus ./scripts/azure-setup.sh
```

The first attempt in East US 2 failed with "not accepting creation of new Windows Azure SQL
Database servers"; Azure restricts some regions for new subscriptions. Central US worked. The
live site is `https://civicbudget-app.ashysmoke-cd0f52a4.centralus.azurecontainerapps.io`.

The script creates the resource group and the template above, generates the SQL and demo
passwords, registers an app for GitHub's OIDC sign-in with a federated credential for this
repository's `main` branch, and stores the ids as repository secrets plus the `AZURE_DEPLOY`
variable that the workflow keys on. It prints the site URL and the demo password; put the
password in the README's "Live demo" section.

Before running it, the `deploy-azure` workflow must have pushed an image at least once (it does
on every push to `main`) and the `civic-budget` package must be public (GitHub → Packages →
civic-budget → Package settings → Change visibility), because the Container App pulls it
anonymously.

## Every day after

Push to `main`. The workflow builds `ghcr.io/spencersmithsite/civic-budget:<sha>`, updates the
app and the reset job to it, and waits for `/health`.

```bash
az containerapp job start -n civicbudget-reset -g civicbudget-rg      # reset the demo now
az containerapp logs show -n civicbudget-app -g civicbudget-rg --tail 100
az containerapp job execution list -n civicbudget-reset -g civicbudget-rg -o table
az group delete -n civicbudget-rg                                     # tear everything down
```

## What to expect

- The first request after an idle hour takes 30 to 60 seconds: the database resumes from
  auto-pause and the container starts (the startup probe allows a few minutes for migrations).
- One replica, always: the output cache and the Blazor circuits live in process (ADR-0021).
- The nightly reset drops the Data Protection keys with everything else, so every session ends
  at 08:00 UTC. For a demo that is a feature.
- If the free SQL limit is ever exhausted mid-month the database pauses until the month rolls
  over; the site shows the friendly error page until then. At the demo's traffic that would take
  well over a hundred hours of continuous use.
