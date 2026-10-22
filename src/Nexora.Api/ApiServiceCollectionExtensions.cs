using System.Text.Json.Serialization;
using FluentValidation;
using Nexora.Api.Authentication;
using Nexora.Api.Endpoints;
using Nexora.Api.Middleware;
using Nexora.Api.OpenApi;
using Nexora.Api.RateLimiting;
using Nexora.Application.Abstractions;

namespace Nexora.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Validation error keys in camelCase (matching the JSON contract) and messages in one language
        // regardless of the server's OS culture.
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => member?.Name;
        ValidatorOptions.Global.LanguageManager.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
            ctx.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(ctx.HttpContext);
            if (!ctx.ProblemDetails.Extensions.ContainsKey("code"))
            {
                ctx.ProblemDetails.Extensions["code"] = ctx.ProblemDetails.Status switch
                {
                    StatusCodes.Status401Unauthorized => "unauthorized",
                    StatusCodes.Status403Forbidden => "forbidden",
                    StatusCodes.Status404NotFound => "not_found",
                    StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
                    _ => "error",
                };
            }
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<IAccessPolicy, HttpAccessPolicy>();
        services.AddNexoraAuthentication();
        services.AddNexoraAuthorization();
        services.AddNexoraRateLimiting(configuration);

        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            o.AddOperationTransformer<IdempotencyHeaderTransformer>();
        });

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Idempotent-Replayed", "X-Cache", "Server-Timing")));

        return services;
    }

    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapTenantEndpoints();
        app.MapOrganizationEndpoints();
        app.MapTransactionEndpoints();
        app.MapReportingEndpoints();
        return app;
    }
}
