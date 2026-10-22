using Nexora.Api.Authentication;
using Nexora.Api.Filters;
using Nexora.Application.DTOs;
using Nexora.Application.Services;

namespace Nexora.Api.Endpoints;

/// <summary>Branches, users and fee rules of the current tenant.</summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var branches = app.MapGroup("/api/v1/branches").WithTags("Branches");

        branches.MapGet("/", async ([AsParameters] BranchListQuery query, BranchService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(query, ct)));

        branches.MapGet("/{id:guid}", async (Guid id, BranchService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct)));

        branches.MapPost("/", async (CreateBranchRequest request, BranchService service, CancellationToken ct) =>
            {
                var branch = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/v1/branches/{branch.Id}", branch);
            })
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<CreateBranchRequest>();

        branches.MapPut("/{id:guid}", async (Guid id, UpdateBranchRequest request, BranchService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<UpdateBranchRequest>();

        var users = app.MapGroup("/api/v1/users").WithTags("Users");

        users.MapGet("/", async ([AsParameters] UserListQuery query, UserService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(query, ct)))
            .RequireAuthorization(Policies.ViewUsers);

        users.MapGet("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(id, ct)))
            .RequireAuthorization(Policies.ViewUsers);

        users.MapPost("/", async (CreateUserRequest request, UserService service, CancellationToken ct) =>
            {
                var user = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/v1/users/{user.Id}", user);
            })
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<CreateUserRequest>();

        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, UserService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<UpdateUserRequest>()
            .WithDescription("Changing role, branch or deactivating a user revokes all of their sessions immediately.");

        var fees = app.MapGroup("/api/v1/fee-rules").WithTags("Fees");

        fees.MapGet("/", async (FeeRuleService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)));

        fees.MapPost("/", async (CreateFeeRuleRequest request, FeeRuleService service, CancellationToken ct) =>
            {
                var rule = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/v1/fee-rules/{rule.Id}", rule);
            })
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<CreateFeeRuleRequest>();

        fees.MapPut("/{id:guid}", async (Guid id, UpdateFeeRuleRequest request, FeeRuleService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(Policies.ManageTenant)
            .WithValidation<UpdateFeeRuleRequest>();

        app.MapPost("/api/v1/fees/quote", async (FeeQuoteRequest request, FeeRuleService service, CancellationToken ct) =>
                TypedResults.Ok(await service.QuoteAsync(request, ct)))
            .WithTags("Fees")
            .RequireAuthorization(Policies.CreateTransactions)
            .WithValidation<FeeQuoteRequest>()
            .WithSummary("Preview the fee for an amount before creating a transaction");

        return app;
    }
}
