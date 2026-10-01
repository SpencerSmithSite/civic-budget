# Secure development policy

**Owner:** the security owner · **Reviewed:** yearly

The conventions live in the ADRs (`docs/DECISIONS.md`) and the architecture notes (`docs/ARCHITECTURE.md`). These are the security-relevant ones, each
enforced by the design or by a test rather than by memory.

## Design rules

- **Tenancy.** Data is filtered by government in the DbContext, never by each query. Application
  code never calls `IgnoreQueryFilters()`. The few operator paths that must see every government
  are the email sender, retention, and the full export and removal; each says so in a comment and
  scopes each statement explicitly.
- **Authorization.** Every service that writes checks the caller's role itself, whatever the page
  showed. Every admin page carries a named policy, and a test lists them all.
- **Input.** Requests are validated (FluentValidation). Typed amounts go through one parser. Return
  URLs are checked as local. Uploaded images are validated, re-encoded, and served sandboxed.
- **Output.** Razor encodes by default. Nothing renders raw HTML from data. The Content Security
  Policy allows scripts from this site only, plus one nonce per request. CSV exports guard against
  spreadsheet formulas.
- **Secrets.** Never in the repository. Local development uses user-secrets.
- **Email.** Never contains a password. Links are single-use tokens.
- **Logging.** Security events go to the security log. Passwords, tokens, and codes are never
  logged.
- **Dependencies.** A new package needs an ADR row explaining why. Packages are managed centrally
  and audited at every restore.

## Testing

- Every domain rule and every authorization rule gets a test.
- Integration tests run against a real SQL Server, not an in-memory stand-in, so the tenant filter
  and constraints are tested as they run.
- Every page is opened as every demo role before release (`scripts/screenshots/role-sweep.mjs`).
  It reports every console error, including Content Security Policy violations, and a release
  ships with none.

## Records

- The tests.
- The sweep's output attached to each release's pull request.
