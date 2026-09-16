namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Bound from the "Seed" configuration section. The demo password comes from user-secrets locally
/// (scripts/dev-setup.sh sets it) and is never committed. If it is missing, demo users are skipped
/// with a warning rather than created with a known password.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? DemoPassword { get; set; }
}
