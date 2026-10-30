using Nexora.Api;
using Nexora.Api.Middleware;
using Nexora.Application;
using Nexora.Infrastructure;
using Nexora.Infrastructure.Persistence.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Nexora.Api"));

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// `dotnet Nexora.Api.dll --migrate` applies migrations + reference data and exits (used by the compose "migrator" service / CD).
var migrateOnly = args.Contains("--migrate");
if (migrateOnly || app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(applyMigrations: migrateOnly || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"));

    if (migrateOnly)
    {
        return;
    }
}

// First, so rate limiting, request context and logs all see the real client IP.
app.UseReverseProxySupport(app.Configuration);
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(o =>
{
    o.GetLevel = (http, _, ex) => ex is not null || http.Response.StatusCode >= 500
        ? Serilog.Events.LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;
    o.EnrichDiagnosticContext = (diagnostics, http) => diagnostics.Set("CorrelationId", CorrelationIdMiddleware.Get(http));
});
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("OpenApi:Enabled", true))
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Nexora API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "Nexora API";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<DemoModeMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapApiEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
