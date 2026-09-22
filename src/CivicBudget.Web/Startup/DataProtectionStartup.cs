using Microsoft.AspNetCore.DataProtection;

namespace CivicBudget.Web.Startup;

/// <summary>
/// Data Protection reads its key ring during host startup, through an internal hosted service, and
/// our keys live in SQL Server (see the <see cref="DataProtectionServiceCollectionExtensions"/>
/// call in Program.cs). On the free Azure tier that read lands on a database resuming from
/// auto-pause, EF retries it for the better part of a minute, and none of it runs on a thread that
/// could serve a page: Kestrel had not started listening yet, so the first visitor of the hour got
/// a blank browser rather than the waiting screen. Without that hosted service the provider loads
/// the key ring the first time something protects or unprotects data, which is after the database
/// is up, because the waiting screen itself uses no cookies or antiforgery tokens.
/// </summary>
public static class DataProtectionStartup
{
    private const string EagerLoaderTypeName = "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService";

    /// <summary>Removes the startup key-ring read. Returns false if the framework no longer registers it, which the tests assert against so a future .NET cannot quietly bring the delay back.</summary>
    public static bool DeferKeyRingLoad(this IServiceCollection services)
    {
        ServiceDescriptor? eagerLoader = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.FullName == EagerLoaderTypeName);

        return eagerLoader is not null && services.Remove(eagerLoader);
    }
}
