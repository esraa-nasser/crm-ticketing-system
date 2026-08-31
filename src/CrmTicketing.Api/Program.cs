using CrmTicketing.Api.Configuration;
using CrmTicketing.Api.Infrastructure;
using CrmTicketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddBlazorClientCors(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddTicketAuthorization();

var app = builder.Build();

// Roles must exist before any policy naming them can admit a caller. Awaited here
// rather than in a hosted service so a failure surfaces at the composition root
// where someone can act on it, and after the migrations this project applies by hand.
await app.Services.SeedIdentityRolesAsync(CancellationToken.None);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(CorsPolicies.BlazorClient);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
