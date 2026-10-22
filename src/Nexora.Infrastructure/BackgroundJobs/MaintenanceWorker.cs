using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexora.Application;
using Nexora.Application.Common;
using Nexora.Application.Services;
using Nexora.Infrastructure.Persistence;

namespace Nexora.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs <see cref="MaintenanceService"/> under a PostgreSQL transaction-level advisory lock so only one
/// instance performs housekeeping at a time, even when the API is scaled out.
/// </summary>
public sealed class MaintenanceRunner(IServiceScopeFactory scopes, ILogger<MaintenanceRunner> logger)
{
    private const long AdvisoryLockKey = 0x4E65786F7261; // "Nexora"

    public async Task<MaintenanceReport?> RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoraDbContext>();
        var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceService>();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);

            var acquired = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLockKey}) AS \"Value\"")
                .SingleAsync(token);

            if (!acquired)
            {
                logger.LogDebug("Maintenance skipped: another instance holds the lock");
                return null;
            }

            var report = await maintenance.RunOnceAsync(token);
            await transaction.CommitAsync(token);
            return report;
        }, ct);
    }
}

public sealed class MaintenanceWorker(MaintenanceRunner runner, IOptions<BackgroundJobOptions> options, ILogger<MaintenanceWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.MaintenanceIntervalMinutes));
        do
        {
            try
            {
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance run failed; it will be retried on the next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
