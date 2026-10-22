using Nexora.Api.Authentication;
using Nexora.Api.Filters;
using Nexora.Application.DTOs;
using Nexora.Application.Services;

namespace Nexora.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/v1/platform/tenants")
            .WithTags("Platform")
            .RequireAuthorization(Policies.PlatformAdmin);

        platform.MapGet("/", async ([AsParameters] TenantListQuery query, TenantService tenants, CancellationToken ct) =>
                TypedResults.Ok(await tenants.ListAsync(query, ct)))
            .WithSummary("List all tenants");

        platform.MapGet("/{id:guid}", async (Guid id, TenantService tenants, CancellationToken ct) =>
                TypedResults.Ok(await tenants.GetAsync(id, ct)))
            .WithSummary("Get a tenant");

        platform.MapPost("/", async (CreateTenantRequest request, TenantService tenants, CancellationToken ct) =>
            {
                var tenant = await tenants.CreateAsync(request, ct);
                return TypedResults.Created($"/api/v1/platform/tenants/{tenant.Id}", tenant);
            })
            .WithValidation<CreateTenantRequest>()
            .WithSummary("Create a tenant together with its first administrator");

        platform.MapPatch("/{id:guid}/status", async (Guid id, ChangeTenantStatusRequest request, TenantService tenants, CancellationToken ct) =>
                TypedResults.Ok(await tenants.ChangeStatusAsync(id, request, ct)))
            .WithValidation<ChangeTenantStatusRequest>()
            .WithSummary("Suspend or re-activate a tenant");

        var current = app.MapGroup("/api/v1/tenant").WithTags("Tenant settings").RequireAuthorization(Policies.ManageTenant);

        current.MapGet("/", async (TenantService tenants, CancellationToken ct) => TypedResults.Ok(await tenants.GetCurrentAsync(ct)))
            .WithSummary("Current tenant settings");

        current.MapPut("/", async (UpdateTenantSettingsRequest request, TenantService tenants, CancellationToken ct) =>
                TypedResults.Ok(await tenants.UpdateCurrentAsync(request, ct)))
            .WithValidation<UpdateTenantSettingsRequest>()
            .WithSummary("Update tenant name and webhook");

        return app;
    }
}
