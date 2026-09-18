using Amazon.CDK;
using Amazon.CDK.Assertions;
using CivicBudget.Infra;

namespace CivicBudget.Infra.Tests;

/// <summary>The deploy role trusts exactly one repository on release refs, and no long-lived credential exists anywhere.</summary>
public class GitHubOidcStackTests
{
    private static readonly Template Template = Synth("SpencerSmithSite/civic-budget");

    private static Template Synth(string repository)
    {
        var app = new App();
        return Template.FromStack(new GitHubOidcStack(app, "Oidc", new GitHubOidcStackProps { Repository = repository }));
    }

    [Fact]
    public void Trusts_github_for_this_repository_on_release_tags_or_the_production_environment_only()
    {
        Template.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
        {
            ["RoleName"] = "CivicBudget-GitHubDeploy",
            ["AssumeRolePolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(
                [
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Action"] = "sts:AssumeRoleWithWebIdentity",
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, object> { ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com" },
                            ["StringLike"] = new Dictionary<string, object>
                            {
                                ["token.actions.githubusercontent.com:sub"] = new object[]
                                {
                                    "repo:SpencerSmithSite/civic-budget:ref:refs/tags/v*",
                                    "repo:SpencerSmithSite/civic-budget:environment:production",
                                },
                            },
                        },
                    }),
                ]),
            }),
        });
    }

    [Fact]
    public void No_iam_user_or_access_key_is_created()
    {
        Template.ResourceCountIs("AWS::IAM::User", 0);
        Template.ResourceCountIs("AWS::IAM::AccessKey", 0);
    }

    [Fact]
    public void Deploy_permissions_are_ecr_push_and_assuming_the_cdk_bootstrap_roles_nothing_broader()
    {
        IDictionary<string, IDictionary<string, object>> policies = Template.FindResources("AWS::IAM::Policy");
        string json = System.Text.Json.JsonSerializer.Serialize(policies);

        Assert.DoesNotContain("\"Action\":\"*\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudformation:", json, StringComparison.Ordinal); // CDK's bootstrap roles hold that
        Assert.Contains("ecr:PutImage", json, StringComparison.Ordinal);
        Assert.Contains("sts:AssumeRole", json, StringComparison.Ordinal);
        Assert.Contains(":role/cdk-*", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_is_configurable()
    {
        Template other = Synth("someone/else");
        string json = System.Text.Json.JsonSerializer.Serialize(other.ToJSON());

        Assert.Contains("repo:someone/else:ref:refs/tags/v*", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SpencerSmithSite", json, StringComparison.Ordinal);
    }
}
