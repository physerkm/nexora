using Microsoft.AspNetCore.Http.HttpResults;
using Nexora.Api.Authentication;
using Nexora.Api.Filters;
using Nexora.Application.DTOs;
using Nexora.Application.Services;

namespace Nexora.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transactions").WithTags("Transactions");

        group.MapGet("/", async ([AsParameters] TransactionListQuery query, TransactionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(query, ct)))
            .WithValidation<TransactionListQuery>()
            .WithSummary("Search transactions")
            .WithDescription("Filter by status, type, branch, currency, date and amount range or free text; sort by createdAt, amount, status, reference or type. " +
                             "Use paging=cursor (and the returned nextCursor) for fast keyset pagination over large result sets.");

        group.MapGet("/{id:guid}", async (Guid id, TransactionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct)));

        group.MapGet("/{id:guid}/history", async (Guid id, TransactionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetHistoryAsync(id, ct)))
            .WithSummary("Audit trail of a transaction");

        group.MapPost("/", async Task<Results<Created<TransactionDto>, Ok<TransactionDto>>> (
                CreateTransactionRequest request, TransactionService service, HttpContext http, CancellationToken ct) =>
            {
                var key = http.Request.Headers[IdempotencyFilter<CreateTransactionRequest>.HeaderName].ToString();
                var result = await service.CreateAsync(request, string.IsNullOrWhiteSpace(key) ? null : key, ct);
                return result.Replayed
                    ? TypedResults.Ok(result.Transaction)
                    : TypedResults.Created($"/api/v1/transactions/{result.Transaction.Id}", result.Transaction);
            })
            .RequireAuthorization(Policies.CreateTransactions)
            .WithValidation<CreateTransactionRequest>()
            .WithIdempotency<CreateTransactionRequest>(required: true)
            .WithSummary("Create a pending transaction (requires Idempotency-Key)");

        group.MapPost("/{id:guid}/approve", async (Guid id, ApproveTransactionRequest request, TransactionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ApproveAsync(id, request, ct)))
            .RequireAuthorization(Policies.ReviewTransactions)
            .WithIdempotency<ApproveTransactionRequest>(required: false)
            .WithSummary("Approve a pending transaction (four-eyes: not by its creator)");

        group.MapPost("/{id:guid}/reject", async (Guid id, TransactionReasonRequest request, TransactionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RejectAsync(id, request, ct)))
            .RequireAuthorization(Policies.ReviewTransactions)
            .WithValidation<TransactionReasonRequest>()
            .WithIdempotency<TransactionReasonRequest>(required: false)
            .WithSummary("Reject a pending transaction");

        group.MapPost("/{id:guid}/cancel", async (Guid id, TransactionReasonRequest request, TransactionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CancelAsync(id, request, ct)))
            .RequireAuthorization(Policies.CancelTransactions)
            .WithValidation<TransactionReasonRequest>()
            .WithIdempotency<TransactionReasonRequest>(required: false)
            .WithSummary("Cancel a transaction (operators: own pending only; managers: also approved)");

        return app;
    }
}
