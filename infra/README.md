# infra

Two ways to run CivicBudget in the cloud, both from the same Docker image.

- **`azure/`: the live demo.** A Bicep template for Azure's free tiers: serverless Azure SQL,
  a Container App that scales to zero, and a nightly job that rebuilds the demo data. See
  [azure/README.md](azure/README.md) and ADR-0030.
- **`CivicBudget.Infra/`: AWS, deploy-ready.** An AWS CDK app in C# for the production-shaped
  design: a VPC, SQL Server on RDS in private subnets, the app on Fargate behind a load
  balancer, Secrets Manager, CloudWatch, a spending alarm, and a GitHub OIDC deploy role. I have
  no AWS account behind this repository, so it has never been deployed; CI synthesizes it and
  runs assertion tests on the templates on every commit (ADR-0008, ADR-0023, walkthrough 08).

The CDK app has two stacks. `CivicBudget-GitHubOidc` is deployed once, by hand: GitHub's OIDC
provider, the deploy role, and the ECR image repository (which has to exist before the first
push). `CivicBudget-App` is everything else, and it is what the deploy workflow updates.

```bash
dotnet build infra/CivicBudget.Infra
cd infra/CivicBudget.Infra && npx aws-cdk@2 synth          # no credentials needed
dotnet test tests/CivicBudget.Infra.Tests                    # 18 assertions on the templates

# With an account (once):
npx aws-cdk@2 bootstrap
npx aws-cdk@2 deploy CivicBudget-GitHubOidc                  # then set AWS_DEPLOY_ROLE_ARN in GitHub
# Then push a v* tag, or:
npx aws-cdk@2 deploy CivicBudget-App -c imageTag=<sha> -c alertEmail=you@example.com
npx aws-cdk@2 destroy CivicBudget-App                        # leaves nothing billing
```
