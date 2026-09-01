using System.Net;
using CrmTicketing.Client.Services;

namespace CrmTicketing.Client.Tests.Services;

public sealed class BearerTokenHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CapturingHandler Inner) CreateClient(TokenStore tokens)
    {
        var inner = new CapturingHandler();
        var handler = new BearerTokenHandler(tokens) { InnerHandler = inner };

        return (new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, inner);
    }

    [Fact]
    public async Task AttachesTheStoredToken()
    {
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), ["Agent"]);
        var (client, inner) = CreateClient(tokens);

        await client.GetAsync(new Uri("api/tickets", UriKind.Relative));

        var header = inner.LastRequest?.Headers.Authorization;
        Assert.NotNull(header);
        Assert.Equal("Bearer", header.Scheme);
        Assert.Equal("a-token", header.Parameter);
    }

    [Fact]
    public async Task SendsNoHeaderWhenSignedOut()
    {
        var (client, inner) = CreateClient(new TokenStore());

        await client.GetAsync(new Uri("api/tickets", UriKind.Relative));

        // No header at all rather than an empty one: the API must see an anonymous
        // request, not a malformed token.
        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task StopsSendingTheTokenAfterClear()
    {
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), ["Agent"]);
        var (client, inner) = CreateClient(tokens);

        await client.GetAsync(new Uri("api/tickets", UriKind.Relative));
        Assert.NotNull(inner.LastRequest?.Headers.Authorization);

        tokens.Clear();
        await client.GetAsync(new Uri("api/tickets", UriKind.Relative));

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public void TokenStore_ReportsSignedInOnlyWhenATokenIsHeld()
    {
        var tokens = new TokenStore();
        Assert.False(tokens.IsSignedIn);

        tokens.Set("a-token", "agent@example.com", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), ["Agent"]);
        Assert.True(tokens.IsSignedIn);
        Assert.Equal("agent@example.com", tokens.Email);

        tokens.Clear();
        Assert.False(tokens.IsSignedIn);
        Assert.Empty(tokens.Roles);
    }
}
