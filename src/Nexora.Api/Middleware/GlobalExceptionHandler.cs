using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Nexora.Application.Common;
using Nexora.Domain.Common;

namespace Nexora.Api.Middleware;

/// <summary>
/// Maps exceptions to RFC 9457 ProblemDetails with a stable machine readable <c>code</c>.
/// Unexpected errors are logged with full detail but returned to the client without internals.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, "validation_failed", e.Message),
            BadRequestException e => (StatusCodes.Status400BadRequest, e.Code, e.Message),
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Code, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            ConcurrencyException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            InvalidStateTransitionException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            UniqueConstraintException => (StatusCodes.Status409Conflict, "duplicate", "A resource with the same unique value already exists."),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            BadHttpRequestException e => (e.StatusCode, "bad_request", "The request is malformed."),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "client_closed_request", "The client closed the request."),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {StatusCode} {ErrorCode}: {Message}", status, code, exception.Message);
        }

        http.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : "Error",
            Detail = detail,
            Extensions = { ["code"] = code },
        };

        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, Exception = exception, ProblemDetails = problem });
    }
}
