# infra

AWS CDK (C#) for CivicBudget. Deploy-ready, not deployed (ADR-0008); see
`docs/walkthroughs/08-aws-deploy-ready.md` and ADR-0023.

```bash
dotnet build infra/CivicBudget.Infra
cd infra/CivicBudget.Infra && npx aws-cdk@2 synth          # no credentials needed
dotnet test tests/CivicBudget.Infra.Tests                    # 17 assertions on the templates

# With an account (once):
npx aws-cdk@2 bootstrap
npx aws-cdk@2 deploy CivicBudget-GitHubOidc                  # then set AWS_DEPLOY_ROLE_ARN in GitHub
# Then push a v* tag, or:
npx aws-cdk@2 deploy CivicBudget-App -c imageTag=<sha> -c alertEmail=you@example.com
npx aws-cdk@2 destroy CivicBudget-App                        # leaves nothing billing
```
