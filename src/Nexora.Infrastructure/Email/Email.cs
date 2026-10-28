using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexora.Application.Abstractions;

namespace Nexora.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>SMTP server. When empty, emails are not sent (in Development they are written to the log instead).</summary>
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;

    /// <summary>STARTTLS. Port 587 with TLS is what most providers expect.</summary>
    public bool UseTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@nexora.local";
    public string FromName { get; set; } = "Nexora";
}

/// <summary>
/// In-memory queue drained by <see cref="EmailDispatcher"/>. Emails still queued when the process stops are lost,
/// which is acceptable for what goes through it (a reset link can simply be requested again).
/// </summary>
internal sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

internal sealed class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending email \"{Subject}\" failed", message.Subject);
            }
        }
    }
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort) { EnableSsl = o.UseTls };
        if (!string.IsNullOrEmpty(o.Username))
        {
            client.Credentials = new NetworkCredential(o.Username, o.Password);
        }

        using var mail = new MailMessage(new MailAddress(o.FromAddress, o.FromName), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body,
        };

        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>Used when no SMTP server is configured. Links in emails are secrets, so the body is logged only in Development.</summary>
internal sealed class LoggingEmailSender(IHostEnvironment environment, ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Email (not sent, no SMTP configured) to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        }
        else
        {
            logger.LogWarning("Email \"{Subject}\" was not sent: Email:SmtpHost is not configured", message.Subject);
        }

        return Task.CompletedTask;
    }
}
