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
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<BearerTokenHandler>();

// Signing in is how the token is obtained, so AuthApiClient carries no handler.
builder.Services.AddHttpClient<AuthApiClient>(client =>
    client.BaseAddress = new Uri(apiBaseAddress));

builder.Services.AddHttpClient<ITicketsApiClient, TicketsApiClient>(client =>
        client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<TicketMetadataProvider>();

await builder.Build().RunAsync();
