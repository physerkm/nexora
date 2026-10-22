using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;

namespace Nexora.Api.Filters;

public sealed record IdempotentEndpointMetadata(bool Required);

/// <summary>
/// Implements the <c>Idempotency-Key</c> header (IETF draft-ietf-httpapi-idempotency-key-header):
/// <list type="bullet">
/// <item>first request: reserve the key, run the handler, store status + body;</item>
/// <item>retry with the same payload: replay the stored response (<c>Idempotent-Replayed: true</c>), the handler is not executed;</item>
/// <item>retry while the first is still running: 409; same key with a different payload: 422.</item>
/// </list>
/// Keys are scoped per user and kept for 24 hours.
/// </summary>
public sealed partial class IdempotencyFilter<TRequest>(
    IIdempotencyStore store,
    RequestContext context,
    IOptions<JsonOptions> jsonOptions,
    ILogger<IdempotencyFilter<TRequest>> logger) : IEndpointFilter where TRequest : class
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var required = http.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpointMetadata>()?.Required ?? true;
        var key = http.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return required
                ? Problem(StatusCodes.Status400BadRequest, "idempotency_key_required", $"The {HeaderName} header is required for this operation.")
                : await next(invocation);
        }

        if (!ValidKey().IsMatch(key))
        {
            return Problem(StatusCodes.Status400BadRequest, "idempotency_key_invalid", $"{HeaderName} must be 1-100 characters: letters, digits, '-', '_', ':' or '.'.");
        }

        var request = invocation.Arguments.OfType<TRequest>().FirstOrDefault();
        var json = JsonSerializer.Serialize(request, jsonOptions.Value.SerializerOptions);
        var path = http.Request.Path.Value ?? string.Empty;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{http.Request.Method}\n{path}\n{json}")));

        var reservation = await store.TryReserveAsync(
            context.TenantId, context.RequireUserId(), key, http.Request.Method, path, hash, Retention, http.RequestAborted);

        switch (reservation.Outcome)
        {
            case IdempotencyOutcome.Replay:
                logger.LogInformation("Replaying stored response for idempotency key {IdempotencyKey}", key);
                http.Response.Headers["Idempotent-Replayed"] = "true";
                return Results.Text(reservation.Body ?? string.Empty, "application/json", Encoding.UTF8, reservation.StatusCode);

            case IdempotencyOutcome.InProgress:
                http.Response.Headers.RetryAfter = "1";
                return Problem(StatusCodes.Status409Conflict, "idempotency_in_progress", "A request with this Idempotency-Key is still being processed.");

            case IdempotencyOutcome.PayloadMismatch:
                return Problem(StatusCodes.Status422UnprocessableEntity, "idempotency_key_reused", "This Idempotency-Key was already used with a different request.");
        }

        try
        {
            var result = await next(invocation);
            var inner = result is INestedHttpResult nested ? nested.Result : result;

            if (inner is IStatusCodeHttpResult { StatusCode: < 500 and var status })
            {
                var body = inner is IValueHttpResult { Value: { } value }
                    ? JsonSerializer.Serialize(value, value.GetType(), jsonOptions.Value.SerializerOptions)
                    : null;
                await store.CompleteAsync(reservation.RecordId, status, body, CancellationToken.None);
            }
            else
            {
                await store.ReleaseAsync(reservation.RecordId, CancellationToken.None);
            }

            return result;
        }
        catch
        {
            // Business errors are not cached: the client may fix the cause and retry with the same key.
            await store.ReleaseAsync(reservation.RecordId, CancellationToken.None);
            throw;
        }
    }

    private static IResult Problem(int status, string code, string detail) =>
        TypedResults.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    [GeneratedRegex("^[A-Za-z0-9_:.-]{1,100}$")]
    private static partial Regex ValidKey();
}
