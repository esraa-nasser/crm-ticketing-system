using Bunit;
using Bunit.TestDoubles;
using CrmTicketing.Client.Pages;
using CrmTicketing.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Pages;

/// <summary>
/// The <c>returnUrl</c> handling added by task 3b.
/// </summary>
/// <remarks>
/// The relative-only rule is a security control, not tidiness: navigating to an
/// unchecked <c>returnUrl</c> would make this page an open redirect, sending a user
/// who has just typed their password to another origin under this application's name.
/// </remarks>
public sealed class SignInTests : BunitContext
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class StubAuthHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private BunitNavigationManager Arrange(string uri)
    {
        var body = $$"""
            {
              "accessToken": "a-token",
              "expiresAt": "2026-09-01T10:00:00+00:00",
              "email": "agent@example.com",
              "userId": "{{UserId}}",
              "roles": ["Agent"]
            }
            """;

        var httpClient = new HttpClient(new StubAuthHandler(body))
        {
            BaseAddress = new Uri("https://localhost/"),
        };

        Services.AddSingleton(new AuthApiClient(httpClient));
        Services.AddSingleton(new TokenStore());

        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(uri);

        return navigation;
    }

    private void SubmitSignIn(IRenderedComponent<SignIn> page)
    {
        page.Find("#email").Change("agent@example.com");
        page.Find("#password").Change("a-password");
        page.Find("form").Submit();
    }

    [Fact]
    public void SignIn_StoresTheUserIdFromTheResponse()
    {
        Arrange("http://localhost/signin");

        SubmitSignIn(Render<SignIn>());

        Assert.Equal(UserId, Services.GetRequiredService<TokenStore>().UserId);
    }

    [Theory]
    [InlineData("/tickets/11111111-1111-1111-1111-111111111111")]
    [InlineData("/tickets?status=Open&page=3")]
    public void SignIn_HonoursARelativeReturnUrl(string returnUrl)
    {
        var navigation = Arrange($"http://localhost/signin?returnUrl={Uri.EscapeDataString(returnUrl)}");

        SubmitSignIn(Render<SignIn>());

        Assert.Contains(returnUrl.Split('?')[0], navigation.Uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example/steal")]
    public void SignIn_RefusesAnAbsoluteOrProtocolRelativeReturnUrl(string returnUrl)
    {
        var navigation = Arrange($"http://localhost/signin?returnUrl={Uri.EscapeDataString(returnUrl)}");

        SubmitSignIn(Render<SignIn>());

        // Falls back to the ticket list rather than leaving the origin. "//host" is
        // the case a naive "is it relative?" check misses.
        Assert.DoesNotContain("evil.example", navigation.Uri, StringComparison.Ordinal);
        Assert.EndsWith("tickets", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void SignIn_WithNoReturnUrl_GoesToTheList()
    {
        var navigation = Arrange("http://localhost/signin");

        SubmitSignIn(Render<SignIn>());

        Assert.EndsWith("tickets", navigation.Uri, StringComparison.Ordinal);
    }
}
