# CivicBudget

A multi-tenant budgeting and public-transparency web application for local
governments (cities, villages, townships, counties), built with .NET 10,
Blazor, EF Core, and SQL Server, with a deploy-ready AWS CDK stack.

[![ci](https://github.com/SpencerSmithSite/civic-budget/actions/workflows/ci.yml/badge.svg)](https://github.com/SpencerSmithSite/civic-budget/actions/workflows/ci.yml)

> **Status:** Phase 6 complete: import with a validation preview, XLSX export of every grid and report, and three printable reports. Next: deploy-ready AWS infrastructure. See [ROADMAP.md](ROADMAP.md).

**Two audiences, one solution**
- **Admin app** — finance staff and department heads build the annual budget:
  fund/department/account setup, two budget-entry modes, live fund-balance
  checks with Ohio-style appropriation limits, Draft → Proposed → Adopted
  workflow, amendments, audit trail, import/export, reports.
- **Public transparency portal** — citizens browse the *published* budget:
  fast, accessible (WCAG 2.1 AA target), no login, works on a phone and
  without JavaScript.

All data is fictional (Village of Maple Ridge, Ohio).

## Run locally

Prerequisites: .NET SDK 10, Docker Desktop (on Apple Silicon, enable *Use Rosetta for x86_64/amd64 emulation*).

```bash
./scripts/dev-setup.sh                      # once: generates a SQL password into .env and user-secrets
docker compose up -d                        # SQL Server 2022, healthy in ~15–30 s
dotnet run --project src/CivicBudget.Web    # migrates + seeds, then serves on https://localhost:5001 (and http://localhost:5000)
```

Then open `https://localhost:5001` and log in (accept the ASP.NET Core dev certificate, or use http://localhost:5000). `scripts/dev-setup.sh` prints the demo password once and
keeps it in user-secrets (`dotnet user-secrets list --project src/CivicBudget.Web`).

| Login | Role | Sees |
|---|---|---|
| `admin@mapleridge.example` | Administrator | Users, government settings, setup |
| `finance@mapleridge.example` | Finance Director | Setup; budget entry in both modes; beginning balances |
| `police@mapleridge.example` | Department Head (Police) | Own department's lines, grouped view |
| `streets@mapleridge.example` | Department Head (Streets, Parks) | Own departments' lines, grouped view |
| `viewer@mapleridge.example` | Viewer | Read-only overview |
| `admin@pinehollow.example` | Administrator (second tenant) | Pine Hollow only; proves isolation |

The public portal needs no login: open `http://localhost:5000/transparency/maple-ridge-oh`
(Pine Hollow Township is at `/transparency/pine-hollow-twp-oh`). Publish an amendment in the
admin app and the portal shows it on the next request.

To start over with fresh seed data: `docker compose down -v && docker compose up -d`.

```bash
dotnet test                                 # all 387 tests; integration tests start their own SQL Server container
```

Documentation: [Design brief](docs/design/DESIGN-BRIEF.md) · [Spec](docs/SPEC.md) · [Architecture](docs/ARCHITECTURE.md) ·
[Decisions](docs/DECISIONS.md) · [Roadmap](ROADMAP.md) · [Walkthroughs](docs/walkthroughs/) · [Interview prep](docs/INTERVIEW-PREP.md)

## Rights
Copyright © 2026 Spencer Smith. **All rights reserved.** This repository is
published for portfolio review only. It is not licensed for use,
modification, or distribution.

## Branch protection (recommended settings for `main`)
- Require a pull request before merging; require the `ci` status check.
- Block force pushes and deletions.
- (Optional) Require linear history.
