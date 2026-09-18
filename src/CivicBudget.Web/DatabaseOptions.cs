using Microsoft.Data.SqlClient;

namespace CivicBudget.Web;

/// <summary>
/// How the app finds and prepares its database outside a developer's machine. Locally the whole
/// connection string lives in user-secrets. In a container the pieces arrive separately: the host
/// and database name are plain settings, the password is injected by ECS straight from the
/// RDS-managed secret. Composing the string here means no derived "connection string" secret has
/// to be kept in sync when RDS rotates the password.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? Host { get; set; }
    public int Port { get; set; } = 1433;
    public string Name { get; set; } = "CivicBudget";
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>Apply pending EF migrations at startup. Development always does; a single-task demo deploy sets this; a real pipeline runs migrations as a step instead.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Seed the two fictional governments if the database is empty. Development always does; the demo deploy opts in.</summary>
    public bool SeedDemoData { get; set; }

    /// <summary>A full connection string wins; otherwise the parts are assembled. Null when neither is configured.</summary>
    public static string? ResolveConnectionString(IConfiguration configuration)
    {
        string? full = configuration.GetConnectionString("CivicBudget");
        if (!string.IsNullOrWhiteSpace(full))
        {
            return full;
        }

        DatabaseOptions parts = configuration.GetSection(SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (string.IsNullOrWhiteSpace(parts.Host))
        {
            return null;
        }

        return new SqlConnectionStringBuilder
        {
            DataSource = $"{parts.Host},{parts.Port}",
            InitialCatalog = parts.Name,
            UserID = parts.User,
            Password = parts.Password,
            Encrypt = true,
            TrustServerCertificate = true, // RDS presents an Amazon-signed certificate; the container image carries no RDS CA bundle
            ConnectTimeout = 30,
        }.ConnectionString;
    }
}
