using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Web.Components;

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

builder.Services.AddInfrastructure(connectionString);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
app.UseAntiforgery();

// Liveness: the process is up. Readiness: it can also reach the database.
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
