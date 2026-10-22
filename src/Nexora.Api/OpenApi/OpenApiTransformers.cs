using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Nexora.Api.Filters;
using Nexora.Api.Middleware;

namespace Nexora.Api.OpenApi;

/// <summary>Adds the JWT bearer scheme so Swagger UI shows the "Authorize" button and sends the token.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Nexora API",
            Version = "v1",
            Description = "Multi-tenant operations and payments platform. Sign in via POST /api/v1/auth/login, then use the access token as a Bearer token. " +
                          $"Platform admins can act on a specific tenant by sending the {RequestContextMiddleware.TenantHeader} header.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from /api/v1/auth/login",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}

/// <summary>Documents the Idempotency-Key header on endpoints that use <see cref="IdempotencyFilter{TRequest}"/>.</summary>
internal sealed class IdempotencyHeaderTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentEndpointMetadata>().FirstOrDefault();
        if (metadata is null)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyFilter<object>.HeaderName,
            In = ParameterLocation.Header,
            Required = metadata.Required,
            Description = "Unique key (e.g. a UUID) per logical operation. Retries with the same key and payload return the original response.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 100 },
        });
        return Task.CompletedTask;
    }
}
