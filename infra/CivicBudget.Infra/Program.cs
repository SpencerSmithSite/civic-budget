using Amazon.CDK;
using CivicBudget.Infra;

// The CDK app: `cdk synth` runs this (see cdk.json) and writes CloudFormation to cdk.out.
// Everything that varies is CDK context, passed as `-c key=value` or set in cdk.context.json:
//   imageTag        ECR tag to run (the deploy workflow passes the git SHA)   default: latest
//   certificateArn  ACM certificate for HTTPS                                 default: none (HTTP)
//   alertEmail      where the monthly spend alarm goes                        default: no alarm
//   repository      GitHub owner/name allowed to deploy                       default: SpencerSmithSite/civic-budget
var app = new App();

var env = new Amazon.CDK.Environment
{
    // Resolved from the credentials in use at synth/deploy time; unset (and fine) in CI's credential-less synth.
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION") ?? "us-east-2", // Ohio
};

_ = new GitHubOidcStack(app, "CivicBudget-GitHubOidc", new GitHubOidcStackProps
{
    Env = env,
    Repository = Context(app, "repository") ?? "SpencerSmithSite/civic-budget",
});

_ = new CivicBudgetStack(app, "CivicBudget-App", new CivicBudgetStackProps
{
    Env = env,
    ImageTag = Context(app, "imageTag") ?? "latest",
    CertificateArn = Context(app, "certificateArn"),
    AlertEmail = Context(app, "alertEmail"),
});

app.Synth();

static string? Context(App app, string key) => app.Node.TryGetContext(key) as string;
