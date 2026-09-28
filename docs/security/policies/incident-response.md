# Incident response policy

**Owner:** the security owner · **Reviewed:** yearly, and after every incident · **Exercise:** a
tabletop exercise once a year

## What counts as an incident

Anything that did, or could have, exposed, changed, or destroyed a government's data, or made the
service unavailable to governments. For example:

- a sign-in from an account that should not have one;
- a burst of lockouts or rate-limit refusals;
- a leaked secret;
- a vulnerability being exploited;
- a lost backup;
- an outage of more than an hour.

## Steps

1. **Report.** Anyone who suspects an incident tells the security owner at once. The security owner
   opens an incident record (time, reporter, what was seen).
2. **Triage** within 4 hours: is it real, what is affected, how severe?
   - **Severity 1:** data exposed or changed, or the service is down.
   - **Severity 2:** a control failed but no data was affected.
   - **Severity 3:** a near miss.
3. **Contain.** Depending on what happened:
   - **Accounts.** Disable them (deactivating a user ends their sessions everywhere within 5
     minutes) and reset two-step sign-in.
   - **Secrets.** Rotate them.
   - **Everyone's sessions.** Rotate the Data Protection keys, which signs out everyone.
   - **Traffic.** Block it at the platform.
   - **Fixes.** Deploy through the normal path (an emergency review is allowed).
4. **Investigate.** The evidence:
   - the security log (who signed in, from where, what was exported);
   - the audit trail (what changed, old and new values);
   - the platform logs (kept 30 days);
   - Git history.

   Preserve copies before anything ages out.
5. **Notify.** Affected governments hear within 72 hours of confirmation: what happened, what data,
   what was done, what they should do. Governments decide their own public notice. Where Ohio's
   breach notification law (ORC 1349.19) or a contract requires more, follow it; the government is
   usually the data owner, and the operator supports its notice.
6. **Recover.** Restore from backup if data was lost or changed ([business
   continuity](business-continuity.md)), then confirm with the government.
7. **Review** within two weeks: what happened, why, what changes. Every action gets an owner and a
   date. Severity 1 reviews are shared with the affected governments.

## Records

- The incident record.
- Evidence copies.
- The notifications sent.
- The review and its actions.
