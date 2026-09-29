# Control matrix

Each row is one SOC 2 Trust Services Criterion (2017 criteria, 2022 points of focus), what meets it,
where that lives, and what an auditor would sample as evidence. The last two columns map the same
control to **NIST CSF 2.0** (function.category) and **NIST SP 800-53 Rev. 5**, the catalog GovRAMP
baselines are drawn from.

"Operator" means the company that runs CivicBudget for governments; the policies it follows are in
[policies/](policies/). Code references are paths in this repository.

## Common criteria (Security)

### CC1: Control environment

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC1.1 Integrity and ethical values | Written security policy that everyone with production access acknowledges | [information-security.md](policies/information-security.md) | Signed acknowledgements, yearly | GV.PO | PL-1, PL-4 |
| CC1.2 Oversight | A named security owner reviews controls every quarter and reports gaps | [information-security.md](policies/information-security.md) | Quarterly review notes | GV.OV | PM-1, PM-9 |
| CC1.3 Structure and responsibilities | Shared responsibility between the software, the operator, each government, and the host | [README](README.md#who-is-responsible-for-what) | This page | GV.RR | PM-2 |
| CC1.4 Competence | Secure development practice; reviewers know the conventions | [secure-development.md](policies/secure-development.md), `CLAUDE.md` | Pull request reviews | GV.RR, PR.AT | AT-2, AT-3 |
| CC1.5 Accountability | Every change and every sign-in is tied to a named person | Audit trail, security log, Git history | `AuditEntries`, `SecurityEvents`, commits | GV.RR | AU-3 |

### CC2: Communication and information

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC2.1 Quality information | Audit trail of every financial change (who, when, old, new) written by an interceptor, so no service can forget | `AuditInterceptor`, `[Audited]` | `AuditInterceptorTests`; a line's history on the worksheet; Recent activity on the Overview | DE.CM | AU-2, AU-3, AU-12 |
| CC2.2 Internal communication | Policies, ADRs, and walkthroughs in the repository | `docs/security`, `docs/DECISIONS.md` | Git history of the docs | GV.PO | PL-2 |
| CC2.3 External communication | Vulnerability reporting address; customers told of incidents within 72 hours | [SECURITY.md](../../SECURITY.md), [incident-response.md](policies/incident-response.md) | Incident records | RS.CO | IR-6, IR-7 |

### CC3: Risk assessment

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC3.1 to CC3.3 Objectives, risks, fraud | Yearly risk assessment, including fraud (a user changing a budget unnoticed, which the audit trail and the separation of department and fiscal roles answer) | [information-security.md](policies/information-security.md) | Risk register | ID.RA, GV.RM | RA-3 |
| CC3.4 Changes that affect controls | A new ADR for any non-obvious choice; a test fails when a new table is not reached by the export and removal | `docs/DECISIONS.md`, `SecurityTests.Every_table_is_reached_from_a_government_unless_it_is_shared_on_purpose` | ADR list; test run | ID.RA | RA-3, CM-4 |

### CC4 and CC5: Monitoring and control activities

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC4.1 Ongoing evaluation | CI on every change and weekly: build, tests, CodeQL, package vulnerabilities | `.github/workflows/ci.yml`, `.github/workflows/security.yml` | Workflow run history | ID.IM, DE.CM | CA-7, RA-5 |
| CC4.2 Deficiencies reported | Findings become GitHub issues with an owner and a due date by severity | [vulnerability-management.md](policies/vulnerability-management.md) | Issue history | ID.IM | CA-5 |
| CC5.1 to CC5.3 Control activities through policy and technology | Rules are enforced in code where possible (roles in services, tenant filter, lockout) rather than by instruction | This matrix | Tests below | GV.PO, PR.PS | PL-1, SA-8 |

### CC6: Logical and physical access

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC6.1 Access security | Every query filtered to the signed-in person's government; every save checked | `CivicBudgetDbContext` query filter, `TenantSaveChangesInterceptor` | `TenantIsolationTests` | PR.AA | AC-3, AC-4 |
| CC6.1 | Roles enforced in services and by named policies on every admin page | `Policies`, `BudgetLinePermissions`, each service's role check | `PermissionsTests`, `AdminPagePolicyTests` | PR.AA | AC-3, AC-6 |
| CC6.1 | Passwords of 10+ characters, hashed with PBKDF2 by Identity; five failures lock the account for 15 minutes | `Infrastructure/DependencyInjection.cs` | `SecurityTests.Five_wrong_passwords_lock_the_account_and_the_log_says_so` | PR.AA | IA-5, AC-7 |
| CC6.1 | Two-step sign-in (TOTP) that a government can require for everyone | `RequireMfaMiddleware`, Government settings | `AccountAndOnboardingTests`, `RequireMfaMiddlewareTests` | PR.AA | IA-2(1), IA-2(2) |
| CC6.1 | Encryption at rest | Azure SQL TDE (on by default); `StorageEncrypted` in the CDK stack | `infra/`; `CivicBudgetStackTests` | PR.DS | SC-28 |
| CC6.1 | Secrets in the platform secret store, never in the repository | Secrets Manager (AWS), Container Apps secrets (Azure), user-secrets locally | Repository search; the CDK stack | PR.DS | IA-5(7), SC-12 |
| CC6.2 New users | Accounts only by an Administrator or the operator's `--provision`; the person chooses their own password through a single-use link | `UserAdminService`, `GovernmentProvisioningService`, `AccountEmailService` | Audit events "Sign-in link sent" | PR.AA | AC-2, IA-5(1) |
| CC6.3 Changing and removing access | Role and department changes are audit events; deactivating a user signs them out everywhere within 5 minutes (the security stamp is re-checked that often) | `UserAdminService`, `SessionPolicy.RecheckEvery` | `UserAdminServiceTests`; the quarterly access review | PR.AA | AC-2(3), PS-4 |
| CC6.3 | Quarterly access review by each government's Administrator; the operator reviews its own production access | [access-control.md](policies/access-control.md); the Users page, or `AspNetUsers.csv` and `AspNetUserRoles.csv` in the full export | Review sign-offs | GV.OV, PR.AA | AC-2(j) |
| CC6.4 Physical access | Carved out to the hosting provider | Azure / AWS SOC 2 Type 2 reports | Yearly report review | PR.AA | PE family (inherited) |
| CC6.5 Disposal | Removing a government deletes every row it has, after a final export; security events and emails are purged after a year | `GovernmentDataStore`, `OffboardCommand`, `RetentionService` | `SecurityTests.Removing_a_government_deletes_every_row_it_had_and_nothing_else`, `Retention_...` | PR.DS | MP-6, SI-12 |
| CC6.6 External threats | HTTPS with HSTS; rate limits on sign-in, second step, reset, exports; Content Security Policy with a per-request nonce; no framing | `RateLimits`, `SecurityHeadersMiddleware`, `Program.cs` | `SecurityControlsTests`; response headers | PR.PS, PR.IR | SC-7, SC-8, SC-5, SI-10 |
| CC6.6 | Sessions end after 30 idle minutes; a remembered device after 14 days | `SessionPolicy`, `js/session.js` | `SecurityControlsTests.A_session_ends_...` | PR.AA | AC-11, AC-12 |
| CC6.6 | Connections to a government's ERP: HTTPS only, a key per government kept in the secret store, checked at startup; call bodies never logged | `ErpConnectionsOptionsValidator`, `HttpErpAdapter` | `HttpErpAdapterTests` | PR.DS, PR.AA | SC-8, IA-5(7), AU-3 |
| CC6.7 Data in transit and exports | TLS for every connection; every export is logged with who and where; the full export withholds password hashes, stamps, and two-step keys | `AdminExportEndpoints`, `GovernmentDataStore` | `SecurityTests.The_full_export_...`; Security log "Exported" rows | PR.DS | SC-8, AC-21 |
| CC6.8 Malicious software | Uploaded images validated and re-encoded, served sandboxed; packages checked against advisories at every restore | `UploadedImage.Validate`, `ImageResponse.Harden`, `NuGetAudit` | `AvatarAndLogoTests`; restore logs | PR.PS | SI-3, SI-7 |

### CC7: System operations

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC7.1 Vulnerabilities and configuration | CodeQL on every change and weekly; Dependabot weekly; actions pinned by commit; infrastructure as code | `security.yml`, `dependabot.yml`, `infra/` | Code scanning alerts; Dependabot PRs | ID.RA, PR.PS | RA-5, SI-2, CM-2, CM-6 |
| CC7.2 Monitoring for anomalies | Security log of every sign-in outcome, lockout, refusal, and export; platform logs kept 30 days | `AuditingSignInManager`, `SecurityEventLog`, Log Analytics / CloudWatch | Security log page; `SecurityTests.Every_sign_in_outcome_is_a_security_event` | DE.CM, DE.AE | AU-2, AU-6, SI-4 |
| CC7.3 to CC7.4 Evaluating and responding to incidents | Written incident response: triage, contain, notify within 72 hours, review | [incident-response.md](policies/incident-response.md) | Incident records, tabletop exercise yearly | RS.MA, RS.AN, RS.MI | IR-4, IR-5, IR-8 |
| CC7.5 Recovery | Restore from point-in-time backup; recovery targets stated | [business-continuity.md](policies/business-continuity.md) | Restore test record, yearly | RC.RP | CP-10 |

### CC8 and CC9: Change management and risk mitigation

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| CC8.1 Changes are authorized, tested, approved | Pull requests to a protected main; CI must pass (warnings as errors, formatting, about 900 tests); deploys only from main through OIDC, with no stored cloud keys | [change-management.md](policies/change-management.md), `.github/workflows/` | PR history with checks; deploy runs | PR.PS | CM-3, CM-4, CM-5, SA-10 |
| CC8.1 | Database changes only through reviewed EF migrations; `MigrationTests` check the model and the migrations agree | `Persistence/Migrations`, `MigrationTests` | Test run | PR.PS | CM-3 |
| CC9.1 Business disruption | Backups, recovery targets, a tested restore | [business-continuity.md](policies/business-continuity.md) | Restore test | GV.RM, RC.RP | CP-2, CP-9 |
| CC9.2 Vendors | Hosting, email, and code-hosting providers reviewed yearly (their SOC 2 reports) | [vendor-management.md](policies/vendor-management.md) | Vendor register | GV.SC | SA-9, SR-6 |

## Availability

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| A1.1 Capacity | Serverless SQL and container apps scale with load; health endpoints the platform probes without touching the database | `infra/azure`, `/health`, `/health/startup` | Platform metrics | ID.AM | CP-2(2), SC-5 |
| A1.2 Backups and environment | Point-in-time backups (Azure SQL 7 days; RDS 7 days, snapshot on delete in production); migrations re-run safely | `infra/azure/main.bicep`, `CivicBudgetStack.cs` | Backup configuration | PR.DS, RC.RP | CP-9 |
| A1.3 Recovery testing | Yearly restore of a backup to a new database, checked with the full export | [business-continuity.md](policies/business-continuity.md) | Restore test record | RC.RP | CP-4, CP-9(1) |

## Confidentiality

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| C1.1 Confidential information is protected | Tenant isolation; the public portal reads only published snapshots through its own context; secrets withheld from exports | `PublicPortalDbContext`, `GovernmentDataStore` | `SnapshotQueryServiceTests`, `SecurityTests` | PR.DS | AC-3, AC-21 |
| C1.2 Disposal | `--offboard` after a final export; retention purge; backups age out after their retention period | `OffboardCommand`, `RetentionService`, [data-retention.md](policies/data-retention.md) | Security log "Government removed" and "Old records removed" rows | PR.DS | MP-6, SI-12 |

## Processing integrity (partial)

| Criterion | Control | Where | Evidence | CSF 2.0 | 800-53 |
|---|---|---|---|---|---|
| PI1.2 Inputs | Validation on every request; one money parser for typed amounts | FluentValidation validators, `MoneyText.Parse` | Validator tests | PR.DS | SI-10 |
| PI1.3 Processing | Budget arithmetic in pure, tested builders; optimistic concurrency on budget versions | `ReportBuilder`, `CertificateBuilder`, `BudgetVersion.Revision` | Application and domain tests, `ConcurrencyTests` | PR.DS | SI-7 |
| PI1.4 Outputs | ERP journals are changes with an idempotency key; one open send per year | `BudgetJournalBuilder`, `BudgetTransmission` | `BudgetTransmissionServiceTests` | PR.DS | SI-7 |
