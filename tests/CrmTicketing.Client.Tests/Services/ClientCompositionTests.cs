using CrmTicketing.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Services;

/// <summary>
/// The service registrations from <c>Program.cs</c>, asserted rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// The bearer token is written by the sign-in page and read by
/// <see cref="BearerTokenHandler"/>. If those two hold different
/// <see cref="TokenStore"/> instances, sign-in appears to succeed and every
/// subsequent call still goes out anonymous — the API answers 401 and the UI
/// reports a failure that looks like an expired session.
/// </para>
/// <para>
/// That is not hypothetical: <c>IHttpClientFactory</c> resolves message handlers
/// from its own DI scope, so a <c>Scoped</c> handler and a <c>Scoped</c> store are
/// resolved separately from the ones a component receives.
/// </para>
/// </remarks>
public sealed class ClientCompositionTests
{
    private const string ApiBaseAddress = "https://localhost:7043/";

    /// <summary>
    /// Mirrors <c>Program.cs</c>. Kept in step with it by hand — the WebAssembly host
    /// builder cannot be constructed in a unit test, so this is a replica, and a
    /// divergence between the two is the one thing this file cannot catch.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddHttpClient<SystemApiClient>(client =>
            client.BaseAddress = new Uri(ApiBaseAddress));

        services.AddSingleton<TokenStore>();
        services.AddScoped<BearerTokenHandler>();

        services.AddHttpClient<AuthApiClient>(client =>
            client.BaseAddress = new Uri(ApiBaseAddress));

        services.AddHttpClient<ITicketsApiClient, TicketsApiClient>(client =>
                client.BaseAddress = new Uri(ApiBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>();

        services.AddScoped<TicketMetadataProvider>();

        return services.BuildServiceProvider();
    }

    private static BearerTokenHandler FindBearerHandler(HttpMessageHandler chain)
    {
        var current = chain;

        while (current is DelegatingHandler delegating)
        {
            if (current is BearerTokenHandler bearer)
            {
                return bearer;
            }

            current = delegating.InnerHandler!;
        }

        throw new InvalidOperationException(
            "The ITicketsApiClient handler chain contains no BearerTokenHandler.");
    }

    [Fact]
    public void TheHandlerAndAComponentShareOneTokenStore()
    {
        using var provider = BuildProvider();

        // What a component receives when it injects TokenStore.
        var componentStore = provider.GetRequiredService<TokenStore>();

        // What the ticket client's handler chain actually reads from.
        var chain = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(ITicketsApiClient));

        var handlerStore = FindBearerHandler(chain).Tokens;

        Assert.Same(componentStore, handlerStore);
    }

    [Fact]
    public void ATokenSetByTheSignInPageIsAttachedByTheHandler()
    {
        // The behavioural form of the same assertion: set the token where the sign-in
        // page sets it, and read it back where the handler reads it.
        using var provider = BuildProvider();

        provider.GetRequiredService<TokenStore>().Set(
            "a-token",
            "agent@example.com",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            ["Agent"],
            isStaff: true);

        var chain = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(ITicketsApiClient));

        Assert.Equal("a-token", FindBearerHandler(chain).Tokens.AccessToken);
    }
}
