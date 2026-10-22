using FluentValidation;

namespace Nexora.Api.Filters;

/// <summary>Runs the FluentValidation validator for <typeparamref name="T"/> before the endpoint handler.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "The request body is missing.",
                extensions: new Dictionary<string, object?> { ["code"] = "body_required" });
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            return TypedResults.ValidationProblem(
                result.ToDictionary(),
                detail: "One or more fields are invalid.",
                extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
        }

        return await next(context);
    }
}

public static class FilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();

    /// <summary>Requires (or accepts) an <c>Idempotency-Key</c> header; see <see cref="IdempotencyFilter{T}"/>.</summary>
    public static RouteHandlerBuilder WithIdempotency<T>(this RouteHandlerBuilder builder, bool required = true) where T : class =>
        builder.WithMetadata(new IdempotentEndpointMetadata(required))
            .AddEndpointFilter<IdempotencyFilter<T>>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
