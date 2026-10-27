using System.Globalization;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PocketLedger.Landing.Contact;

public interface IContactMailSender
{
    Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default);
}

public sealed class SmtpContactMailSender(IOptions<ContactOptions> options) : IContactMailSender
{
    private const int TimeoutMilliseconds = 15_000;

    public async Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Smtp.IsConfigured) throw new InvalidOperationException("Contact:Smtp:Host and Contact:Smtp:FromAddress must be configured to send contact messages.");
        using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
        await client.ConnectAsync(settings.Smtp.Host!, settings.Smtp.Port, settings.Smtp.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Smtp.Username)) await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(ContactMail.Create(settings, submission, DateTimeOffset.UtcNow), cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

// Used in Development when no SMTP server is configured, so the form can be exercised end to end without sending mail.
public sealed class LoggingContactMailSender(ILogger<LoggingContactMailSender> logger) : IContactMailSender
{
    public Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Contact form message from {Email} ({Length} characters) was not sent because SMTP is not configured.", submission.Email, submission.Message.Length);
        return Task.CompletedTask;
    }
}

public static class ContactMail
{
    // The visitor never controls From, To or Subject; their address is only offered as Reply-To and the body is always text/plain.
    public static MimeMessage Create(ContactOptions settings, ContactSubmission submission, DateTimeOffset receivedAt)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.Smtp.FromName, settings.Smtp.FromAddress!));
        message.To.Add(MailboxAddress.Parse(settings.RecipientAddress));
        message.ReplyTo.Add(new MailboxAddress(string.Empty, submission.Email));
        message.Subject = "New message from the PocketLedger contact form";
        message.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
        {
            Text = $"""
                Reply-To: {submission.Email}
                Received: {receivedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}

                The text below was written by a website visitor. Treat it as untrusted.
                ----------------------------------------------------------------------

                {submission.Message}
                """
        };
        return message;
    }
}
