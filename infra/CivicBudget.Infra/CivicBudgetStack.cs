using Amazon.CDK;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using CertificateManager = Amazon.CDK.AWS.CertificateManager;

namespace CivicBudget.Infra;

/// <summary>Everything that varies between deployments, read from CDK context in Program.cs.</summary>
public sealed class CivicBudgetStackProps : StackProps
{
    /// <summary>ECR image tag to run; the deploy workflow passes the git SHA.</summary>
    public string ImageTag { get; init; } = "latest";

    /// <summary>An ACM certificate for HTTPS on the load balancer. Without one the ALB serves HTTP only (fine for a demo, said so in the outputs).</summary>
    public string? CertificateArn { get; init; }

    /// <summary>Where the monthly budget alarm emails; no alarm without it.</summary>
    public string? AlertEmail { get; init; }

    /// <summary>Monthly spend that triggers the alarm, in USD.</summary>
    public double MonthlyBudgetUsd { get; init; } = 60;
}

/// <summary>
/// The whole running application in one stack: a VPC, the container image repository, SQL Server
/// on RDS in private subnets, the app on Fargate behind a public load balancer, its secrets, its
/// logs, and a spending alarm. One stack because the pieces have one lifecycle for a demo; a
/// production account would split the network and database from the service so the app can be
/// redeployed without touching the data (see ADR-0023 for the choices made here).
/// </summary>
public sealed class CivicBudgetStack : Stack
{
    public const string DatabaseUser = "civicbudget_admin";
    public const int ContainerPort = 8080;

    public CivicBudgetStack(Construct scope, string id, CivicBudgetStackProps props) : base(scope, id, props)
    {
        Amazon.CDK.Tags.Of(this).Add("Project", "CivicBudget"); // on every taggable resource, for the cost explorer

        // ---- Network -------------------------------------------------------------------------------
        // Two AZs because RDS and the ALB each want two subnets. One NAT gateway (the app pulls its
        // image and reaches Secrets Manager through it); a second is resilience the demo does not pay for.
        var vpc = new Vpc(this, "Vpc", new VpcProps { MaxAzs = 2, NatGateways = 1 });

        // ---- Image repository ----------------------------------------------------------------------
        // Created by the one-time GitHubOidcStack, because the deploy workflow pushes the image before
        // this stack exists; here it is only looked up by name (the task role gets pull access).
        IRepository repository = Repository.FromRepositoryName(this, "Repository", GitHubOidcStack.ImageRepositoryName);

        // ---- Database ------------------------------------------------------------------------------
        // SQL Server Express on the smallest burstable instance: license included, 10 GB database
        // limit, the same engine the app runs on locally. RDS generates and stores the master
        // password in Secrets Manager; nothing here ever sees it. RDS for SQL Server does not
        // create a database on the instance, so EF's Migrate creates CivicBudget on first start.
        var database = new DatabaseInstance(this, "Database", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.SqlServerEx(new SqlServerExInstanceEngineProps { Version = SqlServerEngineVersion.VER_16 }),
            InstanceType = Amazon.CDK.AWS.EC2.InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.MICRO),
            Vpc = vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            Credentials = Credentials.FromGeneratedSecret(DatabaseUser),
            AllocatedStorage = 20,
            StorageType = StorageType.GP3,
            StorageEncrypted = true,
            MultiAz = false,
            PubliclyAccessible = false,
            BackupRetention = Duration.Days(7),
            // A portfolio demo: tearing down must be one command and leave nothing billing. A real
            // deployment sets DeletionProtection = true and RemovalPolicy.SNAPSHOT.
            DeletionProtection = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        // ---- Secrets the app needs besides the database password ----------------------------------
        // The seeded demo users' password. Generated here so it is never in a file or a workflow;
        // read it from the console or the CLI when you want to sign in (output below).
        var demoPassword = new Amazon.CDK.AWS.SecretsManager.Secret(this, "DemoPassword", new SecretProps
        {
            Description = "CivicBudget demo login password (seeded users)",
            GenerateSecretString = new SecretStringGenerator
            {
                PasswordLength = 20,
                RequireEachIncludedType = true, // Identity's default policy wants upper, lower, digit, and symbol
                ExcludeCharacters = "\"'\\/@`",
            },
        });

        // ---- Service -------------------------------------------------------------------------------
        var cluster = new Cluster(this, "Cluster", new ClusterProps { Vpc = vpc, ContainerInsightsV2 = ContainerInsights.ENABLED });
        var logGroup = new LogGroup(this, "Logs", new LogGroupProps { Retention = RetentionDays.ONE_MONTH, RemovalPolicy = RemovalPolicy.DESTROY });

        CertificateManager.ICertificate? certificate = props.CertificateArn is null
            ? null
            : CertificateManager.Certificate.FromCertificateArn(this, "Certificate", props.CertificateArn);

        // The L2 pattern: ALB + target group + Fargate service + task definition, wired together.
        // 0.5 vCPU / 1 GB is the smallest size at which SQL Server's client, EF, and Blazor's
        // circuits are comfortable. One task: see Scaling below.
        var service = new ApplicationLoadBalancedFargateService(this, "Service", new ApplicationLoadBalancedFargateServiceProps
        {
            Cluster = cluster,
            Cpu = 512,
            MemoryLimitMiB = 1024,
            DesiredCount = 1,
            MinHealthyPercent = 100, // start the new task before stopping the old one: no downtime on deploy
            MaxHealthyPercent = 200,
            PublicLoadBalancer = true,
            Certificate = certificate,
            RedirectHTTP = certificate is not null,
            // Migrations run on the first start and SQL Server Express takes a moment to accept logins.
            HealthCheckGracePeriod = Duration.Minutes(3),
            CircuitBreaker = new DeploymentCircuitBreaker { Enable = true, Rollback = true },
            TaskSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
            {
                Image = ContainerImage.FromEcrRepository(repository, props.ImageTag),
                ContainerPort = ContainerPort,
                LogDriver = LogDriver.AwsLogs(new AwsLogDriverProps { LogGroup = logGroup, StreamPrefix = "web" }),
                // Plain settings. The app composes its connection string from these plus the secret
                // password (DatabaseOptions), so no derived connection-string secret exists to rotate.
                Environment = new Dictionary<string, string>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true", // TLS ends at the ALB; trust X-Forwarded-Proto
                    ["Database__Host"] = database.InstanceEndpoint.Hostname,
                    ["Database__Port"] = Token.AsString(database.InstanceEndpoint.Port),
                    ["Database__Name"] = "CivicBudget",
                    ["Database__User"] = DatabaseUser,
                    ["Database__MigrateOnStartup"] = "true", // single task, so startup migration is safe; a pipeline step otherwise
                    ["Database__SeedDemoData"] = "true",     // it is a demo
                },
                // ECS reads these from Secrets Manager at task start and injects them as environment
                // variables. They never appear in the task definition or the template.
                Secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>
                {
                    ["Database__Password"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(database.Secret!, "password"),
                    ["Seed__DemoPassword"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(demoPassword),
                },
            },
        });

        // Only the app may talk to the database, and only on 1433.
        database.Connections.AllowDefaultPortFrom(service.Service, "From the CivicBudget service");

        // Liveness probe for the ALB. Sticky sessions keep a Blazor Server circuit on the task that
        // owns it, which matters the day DesiredCount becomes 2 (WebSockets pass through an ALB as is).
        service.TargetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
        {
            Path = "/health",
            Interval = Duration.Seconds(30),
            HealthyThresholdCount = 2,
            UnhealthyThresholdCount = 3,
        });
        service.TargetGroup.EnableCookieStickiness(Duration.Hours(8));
        service.TargetGroup.SetAttribute("deregistration_delay.timeout_seconds", "30");

        // Scaling: deliberately none. Two things are already in place for a second task (Data
        // Protection keys in SQL Server, sticky sessions); the one thing missing is a shared output
        // cache store for the portal (Redis via Microsoft.AspNetCore.OutputCaching.StackExchangeRedis),
        // because the in-memory store on task B would not see an eviction issued on task A.

        // ---- Spending alarm ------------------------------------------------------------------------
        if (props.AlertEmail is not null)
        {
            _ = new CfnBudget(this, "MonthlyBudget", new CfnBudgetProps
            {
                Budget = new CfnBudget.BudgetDataProperty
                {
                    BudgetName = "CivicBudget-monthly",
                    BudgetType = "COST",
                    TimeUnit = "MONTHLY",
                    BudgetLimit = new CfnBudget.SpendProperty { Amount = props.MonthlyBudgetUsd, Unit = "USD" },
                },
                NotificationsWithSubscribers = new[]
                {
                    new CfnBudget.NotificationWithSubscribersProperty
                    {
                        Notification = new CfnBudget.NotificationProperty
                        {
                            NotificationType = "ACTUAL",
                            ComparisonOperator = "GREATER_THAN",
                            Threshold = 80,
                            ThresholdType = "PERCENTAGE",
                        },
                        Subscribers = new[] { new CfnBudget.SubscriberProperty { SubscriptionType = "EMAIL", Address = props.AlertEmail } },
                    },
                },
            });
        }

        // ---- Outputs -------------------------------------------------------------------------------
        _ = new CfnOutput(this, "ServiceUrl", new CfnOutputProps
        {
            Value = (certificate is null ? "http://" : "https://") + service.LoadBalancer.LoadBalancerDnsName,
            Description = certificate is null ? "The app (HTTP only: pass -c certificateArn=... for HTTPS)" : "The app",
        });
        _ = new CfnOutput(this, "DemoPasswordSecretArn", new CfnOutputProps { Value = demoPassword.SecretArn, Description = "aws secretsmanager get-secret-value --secret-id <this>" });
        _ = new CfnOutput(this, "LogGroupName", new CfnOutputProps { Value = logGroup.LogGroupName });
    }
}
