# Access control policy

**Owner:** the security owner · **Reviewed:** yearly

## Customer users (inside CivicBudget)

Each government controls its own users. The software enforces the rules below; the government's
Administrator decides who gets what.

- **No self sign-up.** An Administrator adds each person. The operator creates only a new
  government's first Administrator (`--provision`).
- **Choose your own password.** A new user gets a single-use link, valid for one day, to choose a
  password. A temporary password typed by an Administrator must be changed at first sign-in.
- **Passwords** are at least 10 characters with upper and lower case, a digit, and a symbol.
  Identity stores a salted PBKDF2 hash. Five wrong attempts lock the account for 15 minutes.
- **Two-step sign-in** (authenticator app) is available to everyone. An Administrator can require
  it for the whole government, which the operator recommends.
- **Sessions** end after 30 minutes without activity, with a two-minute warning. "Remember me"
  keeps a device signed in for up to 14 days; people are told to use it only on their own device.
- **Roles.** Four roles:

  | Role | Can do |
  |---|---|
  | Administrator | Users and settings |
  | Fiscal Officer | The whole budget |
  | Department User | Their own departments only |
  | Viewer | Read only |

  The services check the role on every action.
- **Leavers.** The Administrator deactivates a person on the day they leave. Deactivating ends
  their sessions everywhere within 5 minutes: open pages and cookies re-check the account that often.
- **Quarterly review.** Each Administrator checks the Users page every quarter: everyone still
  there, and in the right role. The security log shows who has been signing in.

## Operator staff (production systems)

- Production access (the cloud portal, the database, the secret store, GitHub admin) goes only to
  named people who run the service. Each has their own account; there are no shared logins.
- Two-step sign-in is required on each of those accounts.
- Deploys run from GitHub Actions through OIDC federation, so no long-lived cloud keys exist.
- Direct database access is for incidents and restores only, is logged by the platform, and is
  written up afterwards.
- Access is removed on the day someone leaves or changes job.
- **Quarterly review.** The security owner lists everyone with production access and confirms each
  one, keeping the list as the record.

## Records

- Each government's security log: sign-ins, failures, lockouts, exports.
- The audit trail: role and department changes.
- The operator's quarterly access lists.
