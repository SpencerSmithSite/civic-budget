# Walkthrough 08 — Deploy-ready on AWS

What Phase 7 built and how to explain it. Nothing here has been deployed
(no account, ADR-0008); everything here is synthesized, asserted, and
built on every commit, and `cdk bootstrap && cdk deploy` is the only step
left.

---

## 1. The container

`Dockerfile` at the repository root is a two-stage build: the SDK image
restores and publishes, the ASP.NET runtime image gets only the published
output (497 MB, most of it the runtime). Restore is its own layer keyed on
the project files, so a code change does not re-download packages. The
image runs as the non-root `app` user on port 8080, trusts
`X-Forwarded-*` from the load balancer, and carries a `HEALTHCHECK` on
`/health`.

Two gotchas worth telling: `.editorconfig` has to be copied into the
build context because it exempts the generated migrations from the
analyzers (warnings are errors), and `.dockerignore` keeps `.env`, tests,
docs, and `infra/` out of the context.

`docker-compose.full.yml` runs the image beside SQL Server with the same
environment variables ECS would inject. It is the one-command demo and the
local proof that the container works outside `dotnet run`.

---

## 2. What the app needed to change

Three things, all in the Web project:

1. **`DatabaseOptions`** composes the connection string from `Database:Host`,
   `Port`, `Name`, `User`, `Password` when no full string is configured.
   Locally the full string stays in user-secrets; in a container the parts
   arrive separately, and the password arrives as an ECS secret straight
   from the RDS-managed secret. No derived "connection string" secret has
   to be kept in sync when RDS rotates the password.
2. **Migrate and seed are opt-in outside Development**
   (`Database:MigrateOnStartup`, `Database:SeedDemoData`). The demo turns
   both on; a real pipeline would run migrations as a step and never seed.
3. **Data Protection keys in SQL Server.** The container logs warned that
   keys were in an ephemeral directory: every restart would sign everyone
   out and invalidate every antiforgery token.
   `PersistKeysToDbContext<CivicBudgetDbContext>` keeps the key ring in a
   `DataProtectionKeys` table (migration `AddDataProtectionKeys`), shared
   by every instance.

---

## 3. The stacks

`infra/CivicBudget.Infra` is a console app the CDK CLI runs (`cdk.json`).
`Program.cs` reads context (`-c imageTag=`, `certificateArn`, `alertEmail`,
`repository`) and builds two stacks.

### `CivicBudget-App` (`CivicBudgetStack.cs`)

Read it top to bottom; each block has a comment saying why:

| Block | What | The choice |
|---|---|---|
| Network | VPC, 2 AZs, 1 NAT | RDS and the ALB each want two subnets; one NAT is the cost/resilience trade for a demo |
| Repository | ECR, scan on push, keep 10 | `EmptyOnDelete` so destroy works |
| Database | RDS SQL Server Express `db.t3.micro`, 20 GB gp3, encrypted, private, 7-day backups | Same engine as local dev; RDS generates the master password into Secrets Manager |
| Secrets | A generated demo-login password | Never in a file or workflow; read it from Secrets Manager when you want to sign in |
| Service | `ApplicationLoadBalancedFargateService`, 0.5 vCPU / 1 GB, private subnets, circuit breaker with rollback | The L2 pattern wires ALB, target group, task, and service; `MinHealthyPercent 100` gives zero-downtime deploys |
| Task settings | `Database__*` as environment, passwords as ECS `Secrets` | Secrets by reference; the template never holds a password |
| Target group | `/health`, sticky 8 h | Blazor Server circuits must return to the task that owns them |
| Scaling | none, on purpose | Sticky sessions and shared keys are ready; the portal's in-memory output cache is the blocker (Redis store) |
| Alarm | AWS Budgets, 80% of $60, email | Only when `alertEmail` is given |
| Outputs | URL, repository URI, demo password secret ARN, log group | What an operator needs after `cdk deploy` |

### `CivicBudget-GitHubOidc` (`GitHubOidcStack.cs`)

Deployed once by hand. Creates the GitHub OIDC provider and a role whose
trust policy accepts only `repo:SpencerSmithSite/civic-budget` on
`refs/tags/v*` or the `production` environment, with `aud =
sts.amazonaws.com`. Its permissions are ECR push to one repository and
`sts:AssumeRole` on the `cdk-*` bootstrap roles. CloudFormation
permissions live in those bootstrap roles, so the GitHub role is narrow,
and there is no `AWS::IAM::User` or access key anywhere. Output: the role
ARN, which becomes the repository variable `AWS_DEPLOY_ROLE_ARN`.

---

## 4. The tests

`tests/CivicBudget.Infra.Tests` synthesizes both stacks in-process with
`Template.FromStack` and asserts on the CloudFormation. The 17 tests are
the questions an auditor asks:

- the database is private, encrypted, backed up, SQL Server Express, and
  reachable only from the service's security group;
- the task runs in private subnets with no public IP and a circuit breaker;
- passwords reach the container as `Secrets`, never as environment
  variables, and no `MasterUserPassword` literal exists in the template;
- logs go to CloudWatch with retention; the ALB is public, checks
  `/health`, and is sticky; HTTP without a certificate, HTTPS with a
  redirect when one is given;
- one NAT gateway; images scanned; a budget only when an email is given;
- everything is `Delete` on destroy; everything is tagged `Project=CivicBudget`;
- the OIDC role trusts one repository on release refs only, creates no
  user or key, and has no `Action: *` or CloudFormation permission.

Two practicalities: the CDK's JSII runtime is one Node process per test
host, so the assembly disables xUnit parallelization; and `dotnet test`
needs Node.js on the machine (ubuntu runners have it).

---

## 5. The workflows

`ci.yml` gained a `cdk-synth` job: build the CDK app, `npx aws-cdk synth`
with no credentials, and `docker build`. The assertion tests run in the
main job with the rest of `dotnet test`.

`deploy.yml` is complete and gated: it runs on `v*` tags or by hand, only
when `vars.AWS_DEPLOY_ROLE_ARN` is set. It asks GitHub for an OIDC token
(`permissions: id-token: write`), assumes the role, logs in to ECR, builds
and pushes the image tagged with the commit SHA, and runs
`cdk deploy CivicBudget-App -c imageTag=$SHA`. The running task definition
therefore says exactly which commit is live.

---

## 6. What it would cost, and how to make it stop

About $90/month at list price (ADR-0023 has the line items); the NAT
gateway is a third of that, RDS and Fargate most of the rest. The budget
alarm is set low so it fires before the bill is a surprise.
`cdk destroy CivicBudget-App` removes every billable resource because the
demo sets `RemovalPolicy.DESTROY` on RDS, the log group, and ECR; the
comments in the stack say what a production account would flip.

---

## 7. Things to read, in order

1. `Dockerfile` and `docker-compose.full.yml`
2. `src/CivicBudget.Web/DatabaseOptions.cs` and the two blocks it changed in `Program.cs`
3. `infra/CivicBudget.Infra/CivicBudgetStack.cs` (top to bottom)
4. `infra/CivicBudget.Infra/GitHubOidcStack.cs` and `.github/workflows/deploy.yml`
5. `tests/CivicBudget.Infra.Tests/CivicBudgetStackTests.cs`
