using CrmTicketing.Client;
using CrmTicketing.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API lives on a different origin than this WebAssembly app, so the base
// address comes from wwwroot/appsettings*.json rather than HostEnvironment.
var apiBaseAddress = builder.Configuration["Api:BaseAddress"]
    ?? throw new InvalidOperationException(
        "Api:BaseAddress is not configured. Set it in wwwroot/appsettings.json.");

builder.Services.AddHttpClient<SystemApiClient>(client =>
    client.BaseAddress = new Uri(apiBaseAddress));
// Singleton, not scoped. IHttpClientFactory resolves message handlers from its own
// DI scope, so a scoped store would give BearerTokenHandler a different instance than
// the sign-in page writes to: sign-in appears to succeed and every call still goes out
// anonymous. In WebAssembly the app is one user in one session, so a singleton is the
// correct lifetime regardless — but it would be wrong under Blazor Server, where one
// process serves many users and a singleton token store would share one user's
// credentials with everyone. ClientCompositionTests pins this.
builder.Services.AddSingleton<TokenStore>();

// Singleton, matching TokenStore. It holds no state of its own and reads a
// singleton, so the two lifetimes cannot diverge - the failure story 08 found when
// a scoped TokenStore gave BearerTokenHandler a different instance than the page.
builder.Services.AddSingleton<Capabilities>();

builder.Services.AddScoped<BearerTokenHandler>();

// Signing in is how the token is obtained, so AuthApiClient carries no handler.
builder.Services.AddHttpClient<AuthApiClient>(client =>
    client.BaseAddress = new Uri(apiBaseAddress));

builder.Services.AddHttpClient<ITicketsApiClient, TicketsApiClient>(client =>
        client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<TicketMetadataProvider>();

await builder.Build().RunAsync();
