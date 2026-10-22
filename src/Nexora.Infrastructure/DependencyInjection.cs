using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexora.Application.Abstractions;
using Nexora.Domain.Entities;
using Nexora.Infrastructure.BackgroundJobs;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Seeding;
using Nexora.Infrastructure.Repositories;
using Nexora.Infrastructure.Security;

namespace Nexora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<NexoraDbContext>(options => DbContextSetup.Configure(options, connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<NexoraDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<NexoraDbContext>();

        // PBKDF2-HMAC-SHA512, 210k iterations (OWASP guidance for SHA-512); Identity rehashes older hashes on sign-in.
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = configuration.GetValue("Security:PasswordHashIterations", 210_000));

        // Redis is optional: without it the same code paths use process memory (fine for a single instance).
        var redis = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "nexora:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddHybridCache();

        services.AddScoped<ITransactionReadRepository, TransactionReadRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ITokenRevocationStore, DistributedTokenRevocationStore>();

        services.AddHttpClient(WebhookEventPublisher.HttpClientName, c => c.DefaultRequestHeaders.UserAgent.ParseAdd("Nexora-Webhooks/1.0"))
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        services.AddScoped<IIntegrationEventPublisher, WebhookEventPublisher>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<MaintenanceRunner>();
        services.AddHostedService<OutboxProcessor>();
        services.AddHostedService<MaintenanceWorker>();

        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<DatabaseInitializer>();

        services.AddHealthChecks()
            .AddDbContextCheck<NexoraDbContext>("database", tags: ["ready"])
            .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

        return services;
    }
}

internal sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.GetStringAsync("health-probe", cancellationToken);
            return HealthCheckResult.Healthy(cache.GetType().Name);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Distributed cache unreachable", ex);
        }
    }
}
