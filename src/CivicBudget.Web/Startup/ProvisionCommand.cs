using CivicBudget.Application.Common;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;

namespace CivicBudget.Web.Startup;

/// <summary>
/// <c>dotnet CivicBudget.Web.dll --provision --name "Village of Cedar Falls" --type Village --slug cedar-falls-oh
/// --admin-name "Jordan Ellis" --admin-email jordan@cedarfalls.example [--state OH] [--fiscal-year-start 1] [--by "Your name"]</c>
/// <para>
/// Sets up a new government and its first Administrator, then exits, like <c>--reseed</c>: the host is
/// built for its configuration and services but never started. It is the vendor's operation, run with
/// the deployment's own credentials, so no page a government's users can reach creates governments.
/// The Administrator gets a link to choose a password; without a mail server the link is printed here.
/// </para>
/// </summary>
public static class ProvisionCommand
{
    public const string Flag = "--provision";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--name") is not { } name || Arg("--slug") is not { } slug || Arg("--admin-name") is not { } adminName || Arg("--admin-email") is not { } adminEmail
            || !Enum.TryParse(Arg("--type") ?? "", ignoreCase: true, out GovernmentType type))
        {
            await Console.Error.WriteLineAsync("Usage: --provision --name <name> --type City|Village|Township|County --slug <portal-address> --admin-name <name> --admin-email <email> [--state OH] [--fiscal-year-start 1] [--by <your name>]");
            return 2;
        }

        int start = int.TryParse(Arg("--fiscal-year-start") ?? "1", System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int month) ? month : 0;
        await DatabaseInitializer.MigrateAsync(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        Result<ProvisionedGovernmentDto> result = await scope.ServiceProvider.GetRequiredService<IGovernmentProvisioningService>().ProvisionAsync(
            new ProvisionGovernmentRequest(name, type, Arg("--state") ?? "OH", start, slug, adminName, adminEmail), Arg("--by") ?? "CivicBudget operations");
        if (result.IsFailure)
        {
            foreach (ValidationError error in result.Errors)
            {
                await Console.Error.WriteLineAsync($"{(error.PropertyName.Length > 0 ? error.PropertyName + ": " : "")}{error.Message}");
            }

            return 1;
        }

        Console.WriteLine($"Set up {name} ({result.Value.GovernmentId}); {adminEmail} is its Administrator.");
        Console.WriteLine(scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Delivers
            ? $"A link to choose a password was emailed to {adminEmail}."
            : $"No mail server is configured, so give {adminName} this link (it works once, for a day): {result.Value.SignInLink}");
        return 0;
    }
}
