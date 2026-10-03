using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Jobs;

namespace SCalenderPlus.Infrastructure.Email;

/// <summary>Sends via SMTP with MailKit; one connection per message (volume is low, retries come from the job queue).</summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = options.Value;

        var mime = new MimeMessage
        {
            Subject = message.Subject,
            Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody(),
        };
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.From));
        if (!MailboxAddress.TryParse(message.To, out var to))
        {
            throw new PermanentJobFailureException("Invalid recipient address.");
        }

        to.Name = message.ToName;
        mime.To.Add(to);

        using var client = new SmtpClient { Timeout = (int)smtp.Timeout.TotalMilliseconds };
        await client.ConnectAsync(smtp.Host, smtp.Port, ToMailKit(smtp.Security), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(smtp.User))
        {
            await client.AuthenticateAsync(smtp.User, smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)ex.StatusCode >= 500)
        {
            // 5xx for the recipient is permanent (unknown mailbox); 4xx is retried.
            throw new PermanentJobFailureException("Recipient rejected by the SMTP server.", ex);
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    private static SecureSocketOptions ToMailKit(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };
}
