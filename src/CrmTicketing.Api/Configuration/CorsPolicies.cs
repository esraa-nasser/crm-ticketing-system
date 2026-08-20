namespace CrmTicketing.Api.Configuration;

/// <summary>
/// CORS wiring for the standalone Blazor WebAssembly client, which is served from
/// a different origin than this API and therefore needs an explicit allow-list.
/// </summary>
public static class CorsPolicies
{
    public const string BlazorClient = "BlazorClient";

    /// <summary>
    /// Registers the <see cref="BlazorClient"/> policy from the
    /// <c>Cors:AllowedOrigins</c> configuration array.
    /// </summary>
    public static IServiceCollection AddBlazorClientCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        return services.AddCors(options => options.AddPolicy(
            BlazorClient,
            policy =>
            {
                if (origins.Length == 0)
                {
                    // No origins configured: deny cross-origin browser calls rather
                    // than silently falling back to AllowAnyOrigin.
                    return;
                }

                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }));
    }
}
