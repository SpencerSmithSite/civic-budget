# Security and compliance

CivicBudget is **designed and built to achieve SOC 2 compliance**. The controls a SOC 2 audit
examines are in the code, the build, and the deployment, and each one is mapped to the evidence an
auditor would ask for. I have not engaged an auditor, so there is no SOC 2 report yet.

## What SOC 2 is, and what "designed to achieve" means

SOC 2 is an audit standard from the AICPA. An independent CPA firm checks a service against the
**Trust Services Criteria**:

- **Security**, always in scope;
- **Availability**;
- **Processing Integrity**;
- **Confidentiality**;
- **Privacy**.

There are two kinds of report:

| Report | What the auditor confirms |
|---|---|
| Type 1 | The controls are designed properly, on one date. |
| Type 2 | The controls worked, over a period of three to twelve months. |

Either way, most of the work falls into two kinds:

- **The controls themselves.** Access, logging, change control, encryption, backups, response to
  incidents.
- **The operator's routine around them.** Policies, reviews, and the records those reviews leave
  behind.

The controls are built. The routine is written down in the policies below, with how often each
thing happens and what record it leaves. An operator that runs CivicBudget and follows those
policies is ready for a Type 1 audit on day one and a Type 2 audit after its first observation
period.

The scope I would put in front of an auditor is **Security, Availability, and Confidentiality**:

- **Processing Integrity** is partly covered: budget arithmetic is pure and tested, and the ERP
  journals are idempotent.
- **Privacy** matters less here: the app holds staff names, work emails, and, for personnel
  budgeting, employee pay. In Ohio, public employees' pay is a public record.

## Who is responsible for what

Three parties share the controls:

| Party | Responsible for |
|---|---|
| **The software** (this repository) | Tenant isolation, roles, two-step sign-in, session limits, the security log, the audit trail, rate limits, security headers, encryption in transit, the full export, removing a government, retention. |
| **The operator** (the company that runs CivicBudget for governments) | Hosting, backups, secrets, monitoring, patching, change control, incident response, vendor review, and running the scheduled jobs (`--maintenance`). The policies below are written for the operator. |
| **Each government** | Who gets an account and which role. Requiring two-step sign-in. Reviewing its security log. Removing people who leave. Its own records retention schedule. |

The hosting provider (Azure or AWS) covers the physical data center and the platform under the
app; both publish their own SOC 2 Type 2 reports, which the operator reviews under
[vendor management](policies/vendor-management.md).

## The controls, briefly

**Only the right people get in**
- Accounts are created by an Administrator (or the operator for a new government); there is no
  sign-up page.
- Passwords are at least 10 characters with mixed character types. Identity stores a salted
  PBKDF2 hash, never the password. No password is ever emailed; new users choose their own through
  a single-use link.
- Two-step sign-in with an authenticator app, which a government can require for everyone.
- Five wrong passwords lock the account for 15 minutes.
- Sign-in, second-step, and password-reset forms are rate limited per address.
- A session ends after 30 idle minutes, with a two-minute warning. "Remember me" keeps a trusted
  device signed in for 14 days.

**Each person sees only what their role allows**
- Four roles, each enforced by the services, not just by hiding buttons:
  - Administrator;
  - Fiscal Officer;
  - Department User (only their own departments);
  - Viewer.
- Every query is filtered to the signed-in person's government (a global EF Core query filter), and
  every save is checked against it. A test proves one government cannot read or write another's
  data.

**Everything is recorded**
- The **audit trail** records every change to budgets and setup: who, when, old value, new value.
- The **security log** records every sign-in, failed sign-in, unknown address, lockout, second
  step, recovery code, sign-out, idle sign-out, password change, export, and refused request.
  Administrators read their own government's log on the Security log page.

**Data is protected**
- HTTPS only, with HSTS.
- Database encryption at rest (Azure SQL transparent data encryption; RDS storage encryption on AWS).
- Secrets live in the platform's secret store, never in the repository.
- A strict Content Security Policy: scripts from this site only, one per-request nonce, no framing.
- A government can **download everything** it has, one CSV file per table. When it leaves, the
  operator exports it one last time and **removes every row** (`--offboard`).

**Changes are controlled**
- Every change goes through a pull request to a protected main branch.
- CI must pass first:
  - build with warnings as errors;
  - formatting;
  - about 900 tests, including a real SQL Server;
  - CodeQL code scanning;
  - a known-vulnerability check on every package.
- Dependabot proposes updates weekly. Actions are pinned by commit.
- Deploys run from main only, through GitHub's OIDC federation, with no stored cloud keys.

## Other frameworks

| Framework | Where it stands |
|---|---|
| **NIST CSF 2.0** | Every control in the [control matrix](control-matrix.md) is tagged with its CSF function and category (Govern, Identify, Protect, Detect, Respond, Recover). |
| **GovRAMP** (formerly StateRAMP) | GovRAMP uses NIST SP 800-53 Rev. 5 baselines. The matrix gives the 800-53 controls each item supports, which is the starting point for a GovRAMP security package. GovRAMP needs a third-party assessor too. |
| **CJIS** | Does not apply: CivicBudget holds no criminal justice information. A police department's budget is money, not case data. |
| **IRS Publication 1075** | Does not apply: no federal tax information. |
| **HIPAA** | Does not apply: insurance appears only as a premium per coverage tier, with no health information. |

## Documents

- [Control matrix](control-matrix.md): each SOC 2 criterion, what satisfies it, where it lives, the
  evidence, and the NIST CSF 2.0 and 800-53 references.
- Policies, written for the operator:
  - [Information security](policies/information-security.md)
  - [Access control](policies/access-control.md)
  - [Change management](policies/change-management.md)
  - [Vulnerability management](policies/vulnerability-management.md)
  - [Secure development](policies/secure-development.md)
  - [Incident response](policies/incident-response.md)
  - [Business continuity and backups](policies/business-continuity.md)
  - [Data retention and disposal](policies/data-retention.md)
  - [Vendor management](policies/vendor-management.md)
- [Reporting a vulnerability](../../SECURITY.md)

## Known gaps

These are the gaps an honest readiness review would list today:

- **No auditor yet.** There is no Type 1 or Type 2 report.
- **Passwords are not checked against breach lists** (such as Have I Been Pwned's range API). The
  length and lockout rules stand in for it.
- **No single sign-on.** Microsoft Entra ID sign-in is planned but not built; until then, two-step
  sign-in is the second factor.
- **One region.** The demo runs in one Azure region with locally redundant backups. A production
  deployment should choose geo-redundant backup storage (see
  [business continuity](policies/business-continuity.md)).
- **Monitoring is the platform's own** (Azure Log Analytics, CloudWatch) with no paging rota; an
  operator with customers adds alerting on the security log (lockout bursts, rate-limit refusals).
