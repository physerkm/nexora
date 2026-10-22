using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Domain.Entities;
using Nexora.Infrastructure.Persistence;

namespace Nexora.Infrastructure.BackgroundJobs;

/// <summary>
/// Delivers pending outbox messages. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so several API
/// instances can run the dispatcher concurrently without delivering the same message twice at the same time.
/// Failures are retried with exponential backoff (see <see cref="OutboxMessage.MarkFailed"/>).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<BackgroundJobOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger)
{
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoraDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var batchSize = options.Value.OutboxBatchSize;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var now = clock.GetUtcNow();

            var batch = await db.OutboxMessages
                .FromSql($"""
                    SELECT * FROM outbox_messages
                    WHERE status = 'Pending' AND next_attempt_at <= {now}
                    ORDER BY next_attempt_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(token);

            foreach (var message in batch)
            {
                try
                {
                    await publisher.PublishAsync(message, token);
                    message.MarkProcessed(clock.GetUtcNow());
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    message.MarkFailed(ex.Message, clock.GetUtcNow(), Random.Shared.NextDouble());
                    logger.LogWarning(ex, "Delivery of outbox message {MessageId} ({EventType}) failed, attempt {Attempt}, status {Status}",
                        message.Id, message.Type, message.Attempts, message.Status);
                }
            }

            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return batch.Count;
        }, ct);
    }
}

public sealed class OutboxProcessor(OutboxDispatcher dispatcher, IOptions<BackgroundJobOptions> options, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.OutboxIntervalSeconds));
        do
        {
            try
            {
                // Drain while full batches keep coming, then wait for the next tick.
                while (await dispatcher.DispatchPendingAsync(stoppingToken) >= options.Value.OutboxBatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Sends events to the tenant's webhook, signed with HMAC-SHA256. Without a webhook the event is only logged.</summary>
internal sealed class WebhookEventPublisher(IHttpClientFactory httpClients, NexoraDbContext db, TimeProvider clock, ILogger<WebhookEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public const string HttpClientName = "webhooks";

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var hook = message.TenantId is { } tenantId
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.WebhookUrl, t.WebhookSecret }).FirstOrDefaultAsync(ct)
            : null;

        if (hook?.WebhookUrl is null)
        {
            logger.LogInformation("Event {EventType} ({MessageId}) for tenant {TenantId} has no webhook subscriber",
                message.Type, message.Id, message.TenantId);
            return;
        }

        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(hook.WebhookSecret ?? string.Empty),
            Encoding.UTF8.GetBytes($"{timestamp}.{message.Payload}")));

        using var request = new HttpRequestMessage(HttpMethod.Post, hook.WebhookUrl)
        {
            Content = new StringContent(message.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Nexora-Event", message.Type);
        request.Headers.Add("X-Nexora-Delivery", message.Id.ToString());
        request.Headers.Add("X-Nexora-Timestamp", timestamp);
        request.Headers.Add("X-Nexora-Signature", $"v1={signature}");

        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
