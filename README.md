# CivicBudget

A multi-tenant budgeting and public-transparency web application for local
governments (cities, villages, townships, counties), built with .NET 10,
Blazor, EF Core, and SQL Server, with a deploy-ready AWS CDK stack.

> **Status:** Phase 0 (planning) complete. See [ROADMAP.md](ROADMAP.md).

**Two audiences, one solution**
- **Admin app** — finance staff and department heads build the annual budget:
  fund/department/account setup, two budget-entry modes, live fund-balance
  checks with Ohio-style appropriation limits, Draft → Proposed → Adopted
  workflow, amendments, audit trail, import/export, reports.
- **Public transparency portal** — citizens browse the *published* budget:
  fast, accessible (WCAG 2.1 AA target), no login, works on a phone and
  without JavaScript.

All data is fictional (Village of Maple Ridge, Ohio).

Documentation: [Spec](docs/SPEC.md) · [Architecture](docs/ARCHITECTURE.md) ·
[Decisions](docs/DECISIONS.md) · [Roadmap](ROADMAP.md)

## Rights
Copyright © 2026 Spencer Smith. **All rights reserved.** This repository is
published for portfolio review only. It is not licensed for use,
modification, or distribution.

## Branch protection (recommended settings for `main`)
- Require a pull request before merging; require the `ci` status check.
- Block force pushes and deletions.
- (Optional) Require linear history.
