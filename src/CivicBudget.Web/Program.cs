using CivicBudget.Application;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Publishing;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Assistant;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using CivicBudget.Web;
using CivicBudget.Web.Caching;
using CivicBudget.Web.Components;
using CivicBudget.Web.Components.Account;
using CivicBudget.Web.Components.Admin;
using CivicBudget.Web.Components.Common;
using CivicBudget.Web.Components.Portal;
using CivicBudget.Web.Security;
using CivicBudget.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Logging: readable text at a developer's terminal; structured JSON everywhere else (one object per
// line, which CloudWatch and any log shipper parse into searchable fields). No third-party package.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "O";
        options.UseUtcTimestamp = true;
    });
}

string connectionString = DatabaseOptions.ResolveConnectionString(builder.Configuration)
    ?? throw new InvalidOperationException(
        "Connection string 'CivicBudget' is not configured. Run scripts/dev-setup.sh (sets it in user-secrets), or set Database:Host and friends.");
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// Composition root: the only place that knows about every layer.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddEmail(builder.Configuration);
builder.Services.AddScoped<IAppLinks, AppLinks>();
builder.Services.AddCivicBudgetRateLimits();

// Azure Container Apps and AWS load balancers end TLS in front of the app and say so in
// X-Forwarded-Proto, and pass the client's address in X-Forwarded-For. Honoring them keeps the
// scheme right in redirects and emailed links, and puts the real address in the security log and
// under the rate limits. The proxies are the only way in, so the known-proxy list is cleared; only
// the last address (the one the proxy itself added) is taken, so a client cannot choose its own.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

// The assistant's model: registered only when the operator has set Assistant:ApiKey (ADR-0047).
builder.Services.AddAssistantModel(builder.Configuration);

// ERP connections the operator configured (Erp:Connections) use the HTTP adapter, for the governments
// they name. Without any, the simulated ERP stands in where the demo data is seeded, since it knows only
// the demo governments. Elsewhere no ERP API is registered, and the sync and send pages offer files alone.
if (CivicBudget.Infrastructure.DependencyInjection.HasErpConnections(builder.Configuration))
{
    builder.Services.AddErpConnections(builder.Configuration);
}
else if (builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.SeedDemoData)}"))
{
    builder.Services.AddSimulatedErp();
}

// --- Authentication: Identity's cookie. ---------------------------------------------------------
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    SessionPolicy.Apply(options);
});
builder.Services.Configure<SecurityStampValidatorOptions>(SessionPolicy.Apply);

// --- Authorization: named policies + the resource-based budget line handler. --------------------
builder.Services.AddAuthorizationBuilder().AddCivicBudgetPolicies();
builder.Services.AddScoped<IAuthorizationHandler, BudgetLineEditHandler>();

// --- Blazor + the pieces that carry the signed-in user into each scope. ------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<CircuitHandler, CurrentUserCircuitHandler>();
builder.Services.AddScoped<AdminPageState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<CivicBudget.Web.Components.Assistant.AssistantPanelState>();
builder.Services.AddScoped<CivicBudget.Application.Assistant.IAssistantToolProvider, CivicBudget.Web.Help.NavigationTools>();

// Output caching for the public portal (ADR-0021): pages are cached per URL and tagged by
// government slug; publishing evicts the tag. Registered after AddInfrastructure so the real
// invalidator replaces the no-op.
builder.Services.AddOutputCache(options => options.AddBasePolicy(policy => policy.AddPolicy<PortalOutputCachePolicy>(), excludeDefaultPolicy: true));
builder.Services.AddSingleton<IPublishedSnapshotCacheInvalidator, OutputCacheSnapshotInvalidator>();

// Data Protection keys in SQL Server, not the container's filesystem, so sign-in cookies and
// antiforgery tokens survive a restart and are shared by every instance (see DataProtectionKeys).
builder.Services.AddDataProtection()
    .SetApplicationName("CivicBudget")
    .PersistKeysToDbContext<CivicBudgetDbContext>();

// ...but not before the server is listening: the key ring is read lazily instead of during host
// startup, which on a resuming database used to cost the first visitor a blank minute (ADR-0031).
builder.Services.DeferKeyRingLoad();

// The database is prepared after the host starts listening (DatabaseStartupService), so a visitor
// who wakes the demo sees a page within seconds instead of a request that hangs for a minute while
// serverless SQL resumes. StartupState is what the waiting page and the probes read.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StartupState>();
builder.Services.AddSingleton<IDatabaseWaker, DatabaseWaker>();
builder.Services.AddHostedService<DatabaseStartupService>();

builder.Services.AddHealthChecks()
    .AddCheck<StartupHealthCheck>("startup", tags: ["startup"]);

WebApplication app = builder.Build();

// `dotnet CivicBudget.Web.dll --reseed`: the nightly demo reset, run as a scheduled job with the
// same image and settings as the site. It rebuilds the database from the seed and exits; the
// web host is built (for configuration and services) but never started.
if (args.Contains("--reseed", StringComparer.Ordinal))
{
    await DatabaseInitializer.ResetAsync(app.Services);
    return;
}

// `dotnet CivicBudget.Web.dll --provision ...`: set up a new government and its first
// Administrator, then exit. See ProvisionCommand for the arguments.
if (args.Contains(ProvisionCommand.Flag, StringComparer.Ordinal))
{
    Environment.ExitCode = await ProvisionCommand.RunAsync(app.Services, args);
    return;
}

// `dotnet CivicBudget.Web.dll --offboard ...`: export, then delete, a government that has left.
// See OffboardCommand for the arguments and the safety checks.
if (args.Contains(OffboardCommand.Flag, StringComparer.Ordinal))
{
    Environment.ExitCode = await OffboardCommand.RunAsync(app.Services, args);
    return;
}

// `dotnet CivicBudget.Web.dll --maintenance`: the daily retention job, scheduled by the platform.
if (args.Contains(MaintenanceCommand.Flag, StringComparer.Ordinal))
{
    Environment.ExitCode = await MaintenanceCommand.RunAsync(app.Services);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Before the status-code pages: the waiting screen is a 503 with a body of its own.
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<WakingUpMiddleware>();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Redirect to HTTPS only where Kestrel itself has an HTTPS port (a developer's machine). In a
// container TLS ends at the load balancer and Kestrel is HTTP only; the middleware would just log
// "failed to determine the https port" on the first request and do nothing.
if (app.Environment.IsDevelopment() || app.Configuration["HTTPS_PORT"] is not null || app.Configuration["ASPNETCORE_HTTPS_PORTS"] is not null)
{
    app.UseHttpsRedirection();
}

// Order matters: authentication populates HttpContext.User, then our middleware copies it into the
// scoped CurrentUserContext that the tenant query filter reads.
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseMiddleware<RequireMfaMiddleware>();
app.UseMiddleware<PortalQuestionGate>();
app.UseRateLimiter();
app.UseMiddleware<PortalResponseMiddleware>();
app.UseOutputCache();
app.UseAntiforgery();

// Liveness: the process is up (the platform's startup probe, so traffic arrives while the database
// is still waking). Startup: migrations and seed are done, answered from memory for the waiting
// page's poll. Neither touches the database on purpose: both are anonymous, and an endpoint that
// opened a connection per call would let anyone keep the free serverless database awake until its
// monthly allowance ran out (ADR-0031).
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/startup", new() { Predicate = check => check.Tags.Contains("startup") });

app.MapStaticAssets();
// Blazor adds its own "frame-ancestors 'self'" policy header; SecurityHeadersMiddleware sets the
// full policy (with frame-ancestors 'none'), so Blazor's is switched off rather than left to win.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);
app.MapIdentityEndpoints();
app.MapPortalEndpoints();
app.MapAdminExportEndpoints();

await app.RunAsync();
