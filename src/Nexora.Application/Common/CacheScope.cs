using Microsoft.Extensions.DependencyInjection;

namespace Nexora.Application.Common;

/// <summary>
/// Runs a HybridCache factory in a DI scope of its own.
/// HybridCache runs one factory for all concurrent callers of a key and lets it finish for the others even
/// when the caller that started it has gone (an aborted request, a page reload). That caller's scoped services,
/// above all its DbContext, are disposed with its request, so a factory must never capture them.
/// </summary>
public static class CacheScope
{
    /// <param name="tenantId">
    /// The tenant the work is limited to (query filters apply), or null for platform-wide work (filters bypassed).
    /// Callers pass the scope they computed from their own request context.
    /// </param>
    public static async Task<T> RunAsync<T>(this IServiceScopeFactory scopes, Guid? tenantId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseSystemScope(tenantId);
        return await work(scope.ServiceProvider);
    }
}
