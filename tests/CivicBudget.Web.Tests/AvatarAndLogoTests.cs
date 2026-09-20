using CivicBudget.Application.Common;
using CivicBudget.Application.Users;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests;

/// <summary>Phase 10: the mark renders as accessible inline SVG; a person shows their picture when they have one and initials otherwise.</summary>
public class AvatarAndLogoTests : BunitContext
{
    private readonly FakeAvatarService _avatars = new();

    public AvatarAndLogoTests() => Services.AddSingleton<IUserAvatarService>(_avatars);

    [Fact]
    public void Logo_is_an_svg_with_a_label_and_the_requested_size()
    {
        IRenderedComponent<Logo> logo = Render<Logo>(p => p.Add(x => x.Size, 36));

        Assert.Equal("36", logo.Find("svg").GetAttribute("width"));
        Assert.Equal("CivicBudget", logo.Find("svg").GetAttribute("aria-label"));
        Assert.Equal("img", logo.Find("svg").GetAttribute("role"));
    }

    [Fact]
    public void Avatar_shows_initials_without_a_picture_and_the_image_with_one()
    {
        IRenderedComponent<Avatar> initials = Render<Avatar>(p => p.Add(x => x.UserId, "u1").Add(x => x.Name, "Alex Rivera (Admin)"));
        Assert.Equal("AR", initials.Find(".cb-avatar").TextContent.Trim());
        Assert.Empty(initials.FindAll("img"));

        IRenderedComponent<Avatar> picture = Render<Avatar>(p => p.Add(x => x.UserId, "u1").Add(x => x.Name, "Alex Rivera").Add(x => x.Version, 42L).Add(x => x.Size, 24));
        Assert.Equal("Account/Avatar/u1?v=42", picture.Find("img").GetAttribute("src"));
        Assert.Contains("width:24px", picture.Find(".cb-avatar").GetAttribute("style"));
    }

    [Fact]
    public void Avatar_asks_the_service_when_no_version_is_given_and_refreshes_on_change()
    {
        _avatars.Versions["u2"] = 7;
        IRenderedComponent<Avatar> avatar = Render<Avatar>(p => p.Add(x => x.UserId, "u2").Add(x => x.Name, "Dana Whitfield"));
        avatar.WaitForAssertion(() => Assert.Equal("Account/Avatar/u2?v=7", avatar.Find("img").GetAttribute("src")));

        _avatars.Versions["u2"] = null;
        _avatars.RaiseChanged();
        avatar.WaitForAssertion(() => Assert.Equal("DW", avatar.Find(".cb-avatar").TextContent.Trim()));
    }

    [Theory]
    [InlineData("Alex Rivera (Admin)", "AR")]
    [InlineData("Chief Morgan Hale", "CH")]
    [InlineData("Cher", "C")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_take_the_first_and_last_word(string? name, string expected) => Assert.Equal(expected, Display.Initials(name));

    private sealed class FakeAvatarService : IUserAvatarService
    {
        public Dictionary<string, long?> Versions { get; } = [];
        public event Action? Changed;
        public void RaiseChanged() => Changed?.Invoke();
        public Task<UserAvatarDto?> GetAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long?> GetVersionAsync(string userId, CancellationToken ct = default) => Task.FromResult(Versions.GetValueOrDefault(userId));
        public Task<Result> SetOwnAsync(byte[] data, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RemoveAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
