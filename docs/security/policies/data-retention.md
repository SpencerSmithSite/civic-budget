# Data retention and disposal policy

**Owner:** the security owner · **Reviewed:** yearly

## What is kept, and for how long

| Data | Kept | Removed by |
|---|---|---|
| Budgets, lines, positions, the chart, ERP data, published snapshots | While the government is a customer | `--offboard` when it leaves |
| The audit trail | While the government is a customer | `--offboard` |
| Users and roles | While the government is a customer; a deactivated user stays so the audit trail can name them | `--offboard` |
| The security log | 1 year | `--maintenance` (daily), and it survives `--offboard` for the rest of its year as the operator's evidence |
| Emails in the outbox | 1 year after they were written, once sent, failed, or held (one still waiting is never removed) | `--maintenance` |
| Platform logs | 30 days | The platform |
| Backups | 7 days point-in-time; longer-term backups as configured | The platform, on schedule |

Governments have their own records retention schedules, set with their records commission. A
budget adopted by resolution is usually kept permanently by the government itself. CivicBudget
does not replace those records. It keeps the working data while the government is a customer, and
the government keeps its official copies (the adopted resolution, the certificate, the full
export).

## Scheduled job

Run `dotnet CivicBudget.Web.dll --maintenance` once a day, as a scheduled job with the same image
and settings as the site (like the demo's nightly reset). Each run records what it removed in the
security log ("Old records removed").

## When a government leaves

1. The government's Administrator takes **Download everything**, if they want their own copy.
2. The operator runs `--offboard --slug <address> --export-to <file.zip> --confirm <address>`,
   which:
   - writes the final export (nothing is deleted unless it was written);
   - deletes every row the government has, in one transaction;
   - records "Government removed" in the security log.
3. The operator hands the export to the government through a secure channel, then deletes the
   operator's copy within 30 days. The handover and the deletion go on record.
4. The data remains in backups until they age out (7 days, or the long-term retention period). The
   government is told this date.

## Disposal of copies

- Exports and restore-test databases are deleted when their purpose ends, and the deletion is
  recorded.
- Storage media are the hosting provider's responsibility, covered by its SOC 2 report.
