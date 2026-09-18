using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace CivicBudget.Infra;

public sealed class GitHubOidcStackProps : StackProps
{
    /// <summary>"owner/name" of the repository allowed to deploy.</summary>
    public string Repository { get; init; } = "SpencerSmithSite/civic-budget";
}

/// <summary>
/// Lets GitHub Actions deploy without any AWS access key in GitHub. GitHub's OIDC provider signs a
/// short-lived token for each workflow run; IAM trusts that provider for this one repository and
/// only for release tags or the production environment, and hands back temporary credentials for
/// a role that can push images and run CDK deployments. Deployed once, by hand, by an account
/// admin; the deploy workflow then only needs the role's ARN (a repository variable, not a secret).
/// </summary>
public sealed class GitHubOidcStack : Stack
{
    public const string ProviderUrl = "https://token.actions.githubusercontent.com";

    public GitHubOidcStack(Construct scope, string id, GitHubOidcStackProps props) : base(scope, id, props)
    {
        Amazon.CDK.Tags.Of(this).Add("Project", "CivicBudget"); // on every taggable resource, for the cost explorer

        // One provider per account. GitHub's certificate thumbprint is no longer required because
        // AWS validates the provider's certificate chain itself.
        var provider = new OpenIdConnectProvider(this, "GitHubProvider", new OpenIdConnectProviderProps
        {
            Url = ProviderUrl,
            ClientIds = ["sts.amazonaws.com"],
        });

        var deployRole = new Role(this, "DeployRole", new RoleProps
        {
            RoleName = "CivicBudget-GitHubDeploy",
            Description = "Assumed by GitHub Actions (OIDC) to build, push, and cdk deploy CivicBudget",
            MaxSessionDuration = Duration.Hours(1),
            AssumedBy = new WebIdentityPrincipal(provider.OpenIdConnectProviderArn, new Dictionary<string, object>
            {
                // Audience must be STS, and the subject must be this repository on a release tag or
                // the production environment. A pull request from a fork can never match.
                ["StringEquals"] = new Dictionary<string, object> { ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com" },
                ["StringLike"] = new Dictionary<string, object>
                {
                    ["token.actions.githubusercontent.com:sub"] = new[]
                    {
                        $"repo:{props.Repository}:ref:refs/tags/v*",
                        $"repo:{props.Repository}:environment:production",
                    },
                },
            }),
        });

        // Push to the one repository (the ECR auth token call is account-wide by design).
        deployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "EcrLogin",
            Actions = ["ecr:GetAuthorizationToken"],
            Resources = ["*"],
        }));
        deployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "EcrPush",
            Actions =
            [
                "ecr:BatchCheckLayerAvailability", "ecr:CompleteLayerUpload", "ecr:InitiateLayerUpload",
                "ecr:PutImage", "ecr:UploadLayerPart", "ecr:BatchGetImage", "ecr:DescribeRepositories",
            ],
            Resources = [Arn.Format(new ArnComponents { Service = "ecr", Resource = "repository", ResourceName = "civicbudget" }, this)],
        }));

        // CDK deploys by assuming the roles `cdk bootstrap` created (lookup, file publishing, deploy),
        // so the GitHub role itself needs no CloudFormation or service permissions at all.
        deployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AssumeCdkBootstrapRoles",
            Actions = ["sts:AssumeRole"],
            Resources = [Arn.Format(new ArnComponents { Service = "iam", Region = "", Resource = "role", ResourceName = "cdk-*" }, this)],
        }));

        _ = new CfnOutput(this, "DeployRoleArn", new CfnOutputProps
        {
            Value = deployRole.RoleArn,
            Description = "Set as the AWS_DEPLOY_ROLE_ARN repository variable in GitHub",
        });
    }
}
