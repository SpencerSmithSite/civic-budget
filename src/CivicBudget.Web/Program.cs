using CivicBudget.Application;
using CivicBudget.Application.Publishing;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using CivicBudget.Web.Caching;
using CivicBudget.Web.Components;
using CivicBudget.Web.Components.Account;
using CivicBudget.Web.Components.Common;
using CivicBudget.Web.Components.Portal;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
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

string connectionString = builder.Configuration.GetConnectionString("CivicBudget")
    ?? throw new InvalidOperationException(
        "Connection string 'CivicBudget' is not configured. Run scripts/dev-setup.sh (sets it in user-secrets).");

// Composition root: the only place that knows about every layer.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

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
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

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

// Output caching for the public portal (ADR-0005): pages are cached per URL and tagged by
// government slug; publishing evicts the tag. Registered after AddInfrastructure so the real
// invalidator replaces the no-op.
builder.Services.AddOutputCache(options => options.AddBasePolicy(policy => policy.AddPolicy<PortalOutputCachePolicy>(), excludeDefaultPolicy: true));
builder.Services.AddSingleton<IPublishedSnapshotCacheInvalidator, OutputCacheSnapshotInvalidator>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CivicBudgetDbContext>("database", tags: ["ready"]);

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only: migrate + seed on start. Production runs migrations as a deploy step.
    await DatabaseInitializer.MigrateAndSeedAsync(app.Services);
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Order matters: authentication populates HttpContext.User, then our middleware copies it into the
// scoped CurrentUserContext that the tenant query filter reads.
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseMiddleware<PortalResponseMiddleware>();
app.UseOutputCache();
app.UseAntiforgery();

// Liveness: the process is up. Readiness: it can also reach the database.
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapIdentityEndpoints();
app.MapPortalEndpoints();

await app.RunAsync();
