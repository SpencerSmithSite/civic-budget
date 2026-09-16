using CivicBudget.Application;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using CivicBudget.Web.Components;
using CivicBudget.Web.Components.Account;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Structured JSON logs: one JSON object per line, which CloudWatch (and any log shipper) parses
// into searchable fields. No third-party logging package needed.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "O";
    options.UseUtcTimestamp = true;
});

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
app.UseAntiforgery();

// Liveness: the process is up. Readiness: it can also reach the database.
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapIdentityEndpoints();

await app.RunAsync();
