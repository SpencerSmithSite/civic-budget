using System.Security.Claims;
using System.Text.RegularExpressions;
using CivicBudget.Application.Common;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Application.Users;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Notifications;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Notifications;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The account side of market readiness: notices written with the change that caused them, reset
/// and welcome links, delivery when a mail server is set up, two-step sign-in, and setting up a new
/// government from nothing.
/// </summary>
[Collection(SqlServerTests.Name)]
public class AccountAndOnboardingTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _streets;
    private string _adminId = null!;
    private string _streetsUserId = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Accounts");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _adminId = (await db.Users.SingleAsync(u => u.Email == "admin@mapleridge.example")).Id;
        _streetsUserId = (await db.Users.SingleAsync(u => u.Email == "streets@mapleridge.example")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope As(string role, string? userId = null, string name = "Test user", params Guid[] departments)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId ?? "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, name));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        foreach (Guid department in departments)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, department.ToString()));
        }

        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    private AsyncServiceScope Anonymous() => _database.CreateScope();

    private async Task<List<OutboxEmail>> OutboxAsync(Guid? government = null)
    {
        await using CivicBudgetDbContext db = _database.CreateContext(government ?? _mapleRidge);
        return await db.OutboxEmails.OrderBy(e => e.CreatedAtUtc).ToListAsync();
    }

    private static (string UserId, string Code) LinkParts(string body)
    {
        Match m = Regex.Match(body, @"ResetPassword\?userId=([^&\s]+)&code=(\S+)");
        return (Uri.UnescapeDataString(m.Groups[1].Value), m.Groups[2].Value);
    }

    [Fact]
    public async Task A_submission_writes_a_notice_to_the_fiscal_authority_and_a_return_to_the_department()
    {
        await using (AsyncServiceScope director = As(Roles.DepartmentHead, _streetsUserId, "Sam Okafor", _streets))
        {
            Result submitted = await director.ServiceProvider.GetRequiredService<Application.Budgets.IDepartmentRequestService>().SubmitAsync(_draft2027, _streets);
            Assert.True(submitted.IsSuccess, string.Join("; ", submitted.Errors.Select(e => e.Message)));
        }

        List<OutboxEmail> notices = await OutboxAsync();
        Assert.Equal(["admin@mapleridge.example", "finance@mapleridge.example"], notices.Select(n => n.ToAddress).Order());
        Assert.All(notices, n =>
        {
            Assert.Equal(EmailKind.DepartmentSubmitted, n.Kind);
            Assert.Equal(EmailStatus.Held, n.Status);                         // no mail server in tests, as in the demo
            Assert.Equal("Streets & Service submitted its FY2027 Original request", n.Subject);
            Assert.Contains($"{TestDatabase.BaseUrl}admin/budgets/{_draft2027}/departments/{_streets}", n.Body, StringComparison.Ordinal);
        });

        await using (AsyncServiceScope officer = As(Roles.FinanceDirector))
        {
            Assert.True((await officer.ServiceProvider.GetRequiredService<Application.Budgets.IDepartmentRequestService>()
                .ReturnAsync(_draft2027, _streets, "Move the playground to Capital Projects.")).IsSuccess);
        }

        OutboxEmail returned = (await OutboxAsync()).Single(e => e.Kind == EmailKind.DepartmentReturned);
        Assert.Equal("streets@mapleridge.example", returned.ToAddress);
        Assert.Contains("Move the playground to Capital Projects.", returned.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reset_link_sets_a_new_password_once_and_an_unknown_address_gets_nothing()
    {
        await using (AsyncServiceScope anonymous = Anonymous())
        {
            IAccountEmailService accounts = anonymous.ServiceProvider.GetRequiredService<IAccountEmailService>();
            await accounts.RequestPasswordResetAsync("nobody@nowhere.example");
            await accounts.RequestPasswordResetAsync("viewer@mapleridge.example");
        }

        OutboxEmail reset = Assert.Single(await OutboxAsync());
        Assert.Equal((EmailKind.PasswordReset, "viewer@mapleridge.example"), (reset.Kind, reset.ToAddress));
        Assert.DoesNotContain(TestDatabase.DemoPassword, reset.Body, StringComparison.Ordinal);
        (string userId, string code) = LinkParts(reset.Body);

        await using AsyncServiceScope scope = Anonymous();
        IAccountEmailService service = scope.ServiceProvider.GetRequiredService<IAccountEmailService>();
        Result first = await service.ResetPasswordAsync(userId, code, "A-New-Password-42!");
        Result again = await service.ResetPasswordAsync(userId, code, "Another-Password-43!");

        Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(e => e.Message)));
        Assert.Contains("expired or has already been used", again.Errors.Single().Message, StringComparison.Ordinal);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser viewer = (await users.FindByIdAsync(userId))!;
        Assert.True(await users.CheckPasswordAsync(viewer, "A-New-Password-42!"));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Kind == AuditKind.Event && a.Description == "Password reset link sent to viewer@mapleridge.example"));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Description == "viewer@mapleridge.example set a new password from an emailed link"));
    }

    [Fact]
    public async Task A_new_user_chooses_their_own_password_from_a_welcome_link()
    {
        await using AsyncServiceScope admin = As(Roles.Admin, _adminId, "Alex Rivera");
        Result<string> created = await admin.ServiceProvider.GetRequiredService<IUserAdminService>()
            .CreateAsync(new CreateUserRequest("clerk@mapleridge.example", "Robin Hale", null, Roles.Viewer, []));

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(e => e.Message)));
        OutboxEmail welcome = Assert.Single(await OutboxAsync());
        Assert.Equal(EmailKind.Welcome, welcome.Kind);
        Assert.Contains("Alex Rivera added you to Village of Maple Ridge's budget in CivicBudget as Viewer", welcome.Body, StringComparison.Ordinal);

        (string userId, string code) = LinkParts(welcome.Body);
        await using AsyncServiceScope anonymous = Anonymous();
        Assert.True((await anonymous.ServiceProvider.GetRequiredService<IAccountEmailService>().ResetPasswordAsync(userId, code, "Robins-Own-Pass-7!")).IsSuccess);
        ApplicationUser robin = (await anonymous.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId))!;
        Assert.True(robin.EmailConfirmed);
        Assert.False(robin.MustChangePassword);
    }

    [Fact]
    public async Task Only_administrators_read_the_outbox()
    {
        await using (AsyncServiceScope anonymous = Anonymous())
        {
            await anonymous.ServiceProvider.GetRequiredService<IAccountEmailService>().RequestPasswordResetAsync("finance@mapleridge.example");
        }

        await using AsyncServiceScope admin = As(Roles.Admin, _adminId);
        await using AsyncServiceScope officer = As(Roles.FinanceDirector);

        OutboxPageDto page = (await admin.ServiceProvider.GetRequiredService<IOutboxService>().ListAsync(0, 25))!;
        Assert.False(page.Delivers);
        Assert.Equal(1, page.Total);
        Assert.Null(await officer.ServiceProvider.GetRequiredService<IOutboxService>().ListAsync(0, 25));
    }

    [Fact]
    public async Task With_a_mail_server_pending_email_is_sent_and_a_refusal_is_retried_then_given_up()
    {
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            db.OutboxEmails.Add(new OutboxEmail(_mapleRidge, EmailKind.PasswordReset, "good@mapleridge.example", "Good", "Hello", "Body", DateTimeOffset.UtcNow, deliver: true));
            db.OutboxEmails.Add(new OutboxEmail(_mapleRidge, EmailKind.PasswordReset, "bad@mapleridge.example", "Bad", "Hello", "Body", DateTimeOffset.UtcNow, deliver: true));
            await db.SaveChangesAsync();
        }

        var transport = new RefusesBadAddresses();
        var sender = new EmailDeliveryService(_database.Services.GetRequiredService<IServiceScopeFactory>(), new EmailSignal(), transport, TimeProvider.System,
            NullLogger<EmailDeliveryService>.Instance);

        int retryAfter = await sender.DeliverPendingAsync(CancellationToken.None);
        for (int i = 1; i < OutboxEmail.MaxAttempts; i++)
        {
            await sender.DeliverPendingAsync(CancellationToken.None);
        }

        List<OutboxEmail> emails = await OutboxAsync();
        Assert.Equal(1, retryAfter);
        Assert.Equal(["good@mapleridge.example"], transport.Sent);
        Assert.Equal(EmailStatus.Sent, emails.Single(e => e.ToAddress == "good@mapleridge.example").Status);
        OutboxEmail bad = emails.Single(e => e.ToAddress == "bad@mapleridge.example");
        Assert.Equal((EmailStatus.Failed, OutboxEmail.MaxAttempts, "Mailbox unavailable"), (bad.Status, bad.Attempts, bad.LastError));
    }

    [Fact]
    public async Task Requiring_two_step_sign_in_flags_everyone_without_it_until_they_set_it_up()
    {
        await using AsyncServiceScope admin = As(Roles.Admin, _adminId);
        ISignInSecurityService security = admin.ServiceProvider.GetRequiredService<ISignInSecurityService>();
        await using AsyncServiceScope officer = As(Roles.FinanceDirector);

        Assert.True((await officer.ServiceProvider.GetRequiredService<ISignInSecurityService>().SetRequireMfaAsync(true)).IsFailure);
        Assert.True((await security.SetRequireMfaAsync(true)).IsSuccess);

        SignInSecurityDto now = await security.GetAsync();
        Assert.True(now.RequireMfa);
        Assert.Equal((0, 5), (now.UsersWithMfa, now.UsersWithoutMfa));

        UserManager<ApplicationUser> users = admin.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserClaimsPrincipalFactory<ApplicationUser> claims = admin.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        ApplicationUser viewer = (await users.FindByEmailAsync("viewer@mapleridge.example"))!;
        Assert.True((await claims.CreateAsync(viewer)).HasClaim(ClaimNames.MfaSetupRequired, "1"));

        // Set up, the flag goes; reset by an administrator (a lost phone), it comes back.
        await users.SetTwoFactorEnabledAsync(viewer, true);
        Assert.False((await claims.CreateAsync(viewer)).HasClaim(ClaimNames.MfaSetupRequired, "1"));
        Assert.True((await security.ResetTwoFactorAsync(viewer.Id)).IsSuccess);
        viewer = (await users.FindByIdAsync(viewer.Id))!;
        Assert.False(viewer.TwoFactorEnabled);
        Assert.True((await claims.CreateAsync(viewer)).HasClaim(ClaimNames.MfaSetupRequired, "1"));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Description == "Two-step sign-in is now required for every user"));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Description == "Reset two-step sign-in for viewer@mapleridge.example"));
    }

    [Fact]
    public async Task A_new_government_is_provisioned_with_an_administrator_who_chooses_a_password_then_follows_the_checklist()
    {
        await using AsyncServiceScope operations = Anonymous();
        IGovernmentProvisioningService provisioning = operations.ServiceProvider.GetRequiredService<IGovernmentProvisioningService>();
        var request = new ProvisionGovernmentRequest("Village of Cedar Falls", GovernmentType.Village, "OH", 1, "cedar-falls-oh", "Jordan Ellis", "jordan@cedarfalls.example");

        Result<ProvisionedGovernmentDto> provisioned = await provisioning.ProvisionAsync(request, "Test operator");
        Result<ProvisionedGovernmentDto> again = await provisioning.ProvisionAsync(request with { AdminEmail = "other@cedarfalls.example" }, "Test operator");

        Assert.True(provisioned.IsSuccess, string.Join("; ", provisioned.Errors.Select(e => e.Message)));
        Assert.Contains("cedar-falls-oh is used by another government", again.Errors.Single().Message, StringComparison.Ordinal);
        Guid cedarFalls = provisioned.Value.GovernmentId;
        OutboxEmail welcome = Assert.Single(await OutboxAsync(cedarFalls));
        Assert.Contains(provisioned.Value.SignInLink, welcome.Body, StringComparison.Ordinal);
        Assert.Contains("added you to Village of Cedar Falls's budget in CivicBudget as Administrator", welcome.Body, StringComparison.Ordinal);

        UserManager<ApplicationUser> users = operations.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser jordan = (await users.FindByIdAsync(provisioned.Value.AdminUserId))!;
        Assert.False(await users.HasPasswordAsync(jordan));
        Assert.True(await users.IsInRoleAsync(jordan, Roles.Admin));

        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, jordan.Id));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Admin));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, cedarFalls.ToString()));
        await using AsyncServiceScope jordansScope = _database.CreateScope(user: new ClaimsPrincipal(identity));
        SetupChecklistDto checklist = await jordansScope.ServiceProvider.GetRequiredService<ISetupChecklistService>().GetAsync();
        Assert.Equal((1, 5, false), (checklist.RequiredDone, checklist.RequiredTotal, checklist.IsReady));
        Assert.Equal("Chart of accounts", checklist.Steps.First(s => !s.Done).Title);

        await using AsyncServiceScope maple = As(Roles.Admin, _adminId);
        Assert.True((await maple.ServiceProvider.GetRequiredService<ISetupChecklistService>().GetAsync()).IsReady);
    }

    private sealed class RefusesBadAddresses : IEmailTransport
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(OutboxEmail email, CancellationToken ct)
        {
            if (email.ToAddress.StartsWith("bad", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Mailbox unavailable");
            }

            Sent.Add(email.ToAddress);
            return Task.CompletedTask;
        }
    }
}
