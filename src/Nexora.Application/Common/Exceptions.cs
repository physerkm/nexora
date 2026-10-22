namespace Nexora.Application.Common;

/// <summary>Base type for expected application errors. The API maps each subtype to a ProblemDetails response.</summary>
public abstract class AppException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string entity, object id)
    : AppException("not_found", $"{entity} '{id}' was not found.");

public sealed class ConflictException(string code, string message) : AppException(code, message);

public sealed class ForbiddenException(string message = "You are not allowed to perform this operation.")
    : AppException("forbidden", message);

public sealed class UnauthorizedException(string code, string message) : AppException(code, message);

public sealed class BadRequestException(string code, string message) : AppException(code, message);

public sealed class ConcurrencyException(Exception? inner = null)
    : AppException("concurrency_conflict", "The resource was modified by another request. Reload it and try again.", inner);

/// <summary>Raised by the persistence layer when a unique index is violated; carries the index/constraint name.</summary>
public sealed class UniqueConstraintException(string? constraintName, Exception inner)
    : Exception($"Unique constraint '{constraintName}' violated.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
