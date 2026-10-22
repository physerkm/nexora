using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.UnitTests.Domain;

public class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Failures_back_off_exponentially_then_give_up()
    {
        var message = OutboxMessage.Create(Guid.NewGuid(), "transaction.created", "{}", Now);
        var delays = new List<double>();

        for (var i = 1; i < OutboxMessage.MaxAttempts; i++)
        {
            message.MarkFailed("boom", Now, jitter: 0);
            Assert.Equal(OutboxStatus.Pending, message.Status);
            delays.Add((message.NextAttemptAt - Now).TotalSeconds);
        }

        Assert.Equal([10, 20, 40, 80, 160, 320, 640], delays);

        message.MarkFailed("boom", Now, jitter: 0);
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(OutboxMessage.MaxAttempts, message.Attempts);
    }

    [Fact]
    public void Jitter_adds_at_most_twenty_percent()
    {
        var message = OutboxMessage.Create(null, "x", "{}", Now);
        message.MarkFailed("boom", Now, jitter: 1);
        Assert.Equal(12, (message.NextAttemptAt - Now).TotalSeconds, precision: 3);
    }

    [Fact]
    public void Processing_clears_the_error()
    {
        var message = OutboxMessage.Create(null, "x", "{}", Now);
        message.MarkFailed("boom", Now, 0);
        message.MarkProcessed(Now);

        Assert.Equal(OutboxStatus.Processed, message.Status);
        Assert.Null(message.LastError);
        Assert.Equal(Now, message.ProcessedAt);
    }
}

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_is_active_until_expiry_or_revocation()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", Now, TimeSpan.FromDays(7), "127.0.0.1");

        Assert.True(token.IsActive(Now.AddDays(6)));
        Assert.False(token.IsActive(Now.AddDays(7)));

        var replacement = Guid.NewGuid();
        token.Revoke("rotated", Now, replacement);
        Assert.False(token.IsActive(Now));
        Assert.Equal(replacement, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revocation_keeps_the_first_reason()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", Now, TimeSpan.FromDays(7), null);
        token.Revoke("rotated", Now);
        token.Revoke("reuse_detected", Now.AddMinutes(1));

        Assert.Equal("rotated", token.RevokedReason);
        Assert.Equal(Now, token.RevokedAt);
    }
}
