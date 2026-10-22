namespace Nexora.Domain.Common;

/// <summary>A business rule was violated. Maps to HTTP 422.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The requested state change is not allowed from the current state. Maps to HTTP 409.</summary>
public sealed class InvalidStateTransitionException(string entity, string from, string to)
    : DomainException("invalid_state_transition", $"{entity} cannot move from '{from}' to '{to}'.")
{
    public string From { get; } = from;
    public string To { get; } = to;
}
