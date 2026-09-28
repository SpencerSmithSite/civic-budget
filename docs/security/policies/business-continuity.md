# Business continuity and backup policy

**Owner:** the security owner · **Reviewed:** yearly · **Restore test:** yearly

## Targets

| | Target | How |
|---|---|---|
| **RPO** (data that may be lost) | 15 minutes or less | The database's point-in-time restore: Azure SQL takes transaction log backups every 5 to 10 minutes; RDS every 5 minutes. |
| **RTO** (time to be back) | 4 hours | Restore the database to a new instance at a point before the problem (one command in the cloud provider's CLI), point the app's connection string at it, and redeploy the same image. The templates in `infra/` recreate everything else. |

The app keeps no state of its own. Its containers are rebuilt from the image, and everything
lives in the database, including the Data Protection keys that sign cookies. A restored database
is therefore a restored service.

## Backups

| Deployment | Backups |
|---|---|
| **Azure** (`infra/azure/main.bicep`) | Automatic full, differential, and log backups; point-in-time restore for 7 days. Transparent data encryption on. The demo uses locally redundant backup storage to stay free; a production deployment sets `requestedBackupStorageRedundancy` to `Geo` so a region outage can be recovered in the paired region, and turns on long-term retention (weekly backups kept 12 weeks, monthly kept 12 months). |
| **AWS** (`infra/CivicBudget.Infra`) | Automated backups kept 7 days, storage encrypted. A production deployment sets `DeletionProtection = true`, `RemovalPolicy.SNAPSHOT`, Multi-AZ, and 35 days of retention. |

A government can also keep its own copy at any time with **Download everything**.

## Restore test

Once a year the security owner:

1. restores the production database to a new database at a chosen time;
2. starts the app against it in a separate environment;
3. takes the full export of one government from both and compares row counts;
4. records how long the restore took against the RTO;
5. deletes the test copy, a disposal that goes in the record too.

## If the region is lost

The operator redeploys the templates to the paired region, restores the geo-replicated backup,
and moves the domain. It then tells governments the expected time back and, afterwards, the actual
data loss.

## Records

- Backup configuration, from the templates.
- The yearly restore test record.
