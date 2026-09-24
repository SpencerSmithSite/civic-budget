# Walkthrough 18: Cold start

Phase 16. The live demo scales to zero and its database pauses after an idle
hour, so the first visitor of the afternoon used to look at a blank page for
about a minute. This phase makes that minute visible and honest: a screen in
the app's style within seconds, and the site appearing on its own when the
database answers.

## 1. What the minute was made of

The Container Apps logs for one cold start, from the request that woke it:

| t+ | event |
|---|---|
| 0s | request arrives; KEDA scales 0 to 1 |
| 14s | replica scheduled, image pulled (127 MB, cached on the node) |
| 16s | container started; the .NET process is up in about 3 seconds |
| 17s to 64s | `GetPendingMigrationsAsync` retried three times while serverless SQL resumed |
| 65s | migrations checked, seed skipped, `Now listening` |
| 67s | startup probe passes; the browser finally receives bytes |

The 15 seconds of platform time are the price of zero replicas; the 48 seconds
are the price of the free database. Neither is avoidable for free. What was
avoidable is the blank page: `Program.cs` awaited `MigrateAsync` before
`RunAsync`, so Kestrel was not even listening, and the startup probe on
`/health` had nothing to hit.

## 2. Listen first, prepare behind

`Web/Startup/` holds the whole change, four small files:

- **`StartupState`**: a singleton with `IsReady` and `ElapsedSeconds` (from a
  `TimeProvider`, so a test can set the clock).
- **`DatabaseStartupService`**: a `BackgroundService` that runs the same
  `DatabaseInitializer.MigrateAsync` and `SeedAsync` the inline code did, under
  the same options, then calls `MarkReady()`. On failure it logs critical and
  stops the host; the platform restarts the container and the next attempt
  starts clean, which is what an inline throw achieved before.
- **`StartupHealthCheck`**: healthy once ready. It answers from memory, so the
  waiting page can poll it every two seconds without a database connection.
- **`WakingUpMiddleware`** and **`WakingUpPage`**: below.

`Program.cs` now maps three health endpoints. `/health` is liveness (the
process is up) and is what the Azure startup probe hits, every two seconds
instead of ten. `/health/startup` is the in-memory check. `/health/ready` is
startup plus a real round trip to the database.

## 2b. The part that did not work the first time

Shipping the above changed nothing: the live site still showed a blank browser
for about seventy seconds. The logs said why, once read in order:

```
21:59:50.928  Applying 0 pending migration(s).
21:59:50.945  Now listening on: http://[::]:8080
21:59:51.439  Database ready after 52s
```

Kestrel started listening **after** the database work, fifty-two seconds in,
which is exactly what the change was supposed to prevent. The blocker was not
in `Startup/` at all: `AddDataProtection` registers an internal hosted service
that reads the key ring while the host starts, the keys live in SQL Server
(so sign-in cookies survive a restart), and EF's retry strategy spent that
minute on the read before the web host service got its turn. Hosted services
registered in `Program.cs` run before the one that starts Kestrel, so anything
of theirs that blocks holds the server back.

`DataProtectionStartup.DeferKeyRingLoad` removes that registration. The
provider then loads the key ring the first time something protects or
unprotects data, which is after the database is up, because the waiting screen
uses no cookies and no antiforgery tokens. Measured against a database address
that hangs, time to the first page went from 31 seconds to 1.

It matches an internal framework type by name, so the test asserts the removal
happened rather than trusting it; a future .NET that renames the type fails the
build instead of quietly restoring the blank minute.

## 3. The waiting screen

The middleware sits before the status-code pages. While the state is not
ready, any request that is not a health probe or a static asset gets
`503 Service Unavailable`, `Retry-After: 5`, `Cache-Control: no-store`, and a
self-contained HTML page: the navy public header with the mark inline, a card
in the app's tokens, two sentences ("This demo environment sleeps when nobody
is using it. The database is starting now, which usually takes under a
minute."), an animated bar, and a counter that starts from the process's own
elapsed seconds so it does not restart on reload. A few lines of script poll
`/health/startup` and `location.reload()` when it returns 200, which lands the
visitor on the URL they asked for. A `noscript` meta refresh covers the rest.
After two minutes a line appears offering a manual refresh.

503 rather than 200 is deliberate: a monitor or crawler that sees the waiting
screen must not record it as the site.

`/_blazor` is not exempt, so no circuit can start against a database that is
not there yet.

## 3b. When the database pauses behind a running container

Scale-to-zero usually retires the container a few minutes after the last
request, long before the database pauses at sixty. But an admin tab left open
holds a WebSocket, and that can keep the container up for hours. Then the app
is "ready", the database is asleep, and the next page load waits on a hung
connection with nothing on screen.

`StartupState` now remembers when it last let a page through. After
`QuietSpell` (55 minutes) with none, the middleware calls `BeginWaiting()`
(only the first request of a burst gets true), starts `DatabaseWaker`, and
waits up to `CheckGrace` (one second). An awake database answers in
milliseconds and the page is served as normal. A sleeping one gets the waiting
screen with its counter restarted, and `/health/startup` flips back to 200
when the waker's connection finally succeeds. The waker runs on its own task,
so a visitor closing the tab does not cancel the wake for the next one.

## 3c. What is left, and why it stays

After a few idle minutes the first visit still waits about 17 seconds before
anything shows. From this morning's logs: 15.2 seconds for Azure to provision
a sandbox, under a second to pull the image, 0.3 seconds of the app's own startup.
Only a replica that is already running avoids it: `minReplicas: 1` for about
$4 to $5 a month, or a weekday business-hours scale rule inside the free
grant. I kept the demo free and scale-to-zero; ADR-0031 records the
numbers so the choice can be revisited with one line of Bicep.

## 4. Seeing it locally

Stop SQL Server, start the app, open any page: the screen appears at once.
`docker compose start`, and within a few seconds of the initializer's
"Database ready" line the browser moves on by itself. The
`WakingUpMiddlewareTests` cover the routing (which paths pass, which get the
screen), the headers, the page content, and the health check.

## 5. Things to read

1. `src/CivicBudget.Web/Startup/DataProtectionStartup.cs` (the fix that made the rest work)
2. `src/CivicBudget.Web/Startup/WakingUpMiddleware.cs` (middleware and page in one file)
3. `src/CivicBudget.Web/Startup/DatabaseStartupService.cs`
4. The health-check block in `src/CivicBudget.Web/Program.cs`
5. `docs/DECISIONS.md` ADR-0031
