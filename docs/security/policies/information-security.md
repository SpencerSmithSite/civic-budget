# Information security policy

**Owner:** the security owner (one named person at the operator) · **Reviewed:** yearly, and after
any security incident · **Applies to:** everyone with access to CivicBudget's source code,
production systems, or customer data.

## Purpose

To protect the budgets, staff records, and settings that governments keep in CivicBudget, and to
keep the service available to them. This policy sets the rules; the other policies say how each
area is run.

## Rules

1. **Least privilege.** People get the access their job needs and no more, reviewed every quarter
   ([access control](access-control.md)).
2. **Customer data stays in production.** No copying a government's data to a laptop, a test
   environment, or a chat tool. Development and demos use the fictional seed data only. The one
   exception is a government's own export handed to that government.
3. **Secrets live in the secret store.** Database passwords, SMTP credentials, and signing keys are
   kept in the platform's secret store and never in source code, tickets, or chat.
4. **Every change goes through review** ([change management](change-management.md)).
5. **Known vulnerabilities are fixed on a clock** ([vulnerability management](vulnerability-management.md)).
6. **Incidents are reported at once** to the security owner, by anyone who suspects one
   ([incident response](incident-response.md)).
7. **Devices** used for production access have full-disk encryption, a screen lock of 10 minutes or
   less, automatic updates, and a password manager. Accounts for GitHub, the cloud provider, and the
   domain registrar use two-step sign-in with an authenticator app or a security key.

## Risk assessment

Once a year, and whenever the service changes shape (a new hosting region, a new integration),
the security owner lists what could go wrong, how likely and how harmful it is, and what reduces
it. The list lives in a risk register. It always considers:

- **One government reading another's data.** The tenant filter and its tests reduce this.
- **A stolen password.** Two-step sign-in, lockout, and the rate limits reduce this.
- **A person changing a budget unnoticed.** The audit trail and the separate department and fiscal
  roles reduce this.
- **A vulnerable package.** The dependency checks reduce this.
- **Losing the database.** Backups and a tested restore reduce this.
- **A vendor failing.** Vendor review reduces this.

## Acknowledgement

Everyone in scope reads this policy and the ones it links when they start, and again each year,
and records that they did.

## Records

- Signed acknowledgements.
- The risk register.
- The quarterly review notes: what was checked, what was found, who owns each gap.
