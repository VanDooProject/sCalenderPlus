namespace SCalenderPlus.Application.Email;

/// <summary>
/// Delivers an email synchronously (SMTP in Infrastructure). Only the worker's <c>email.send</c> job calls it;
/// use cases queue emails with <see cref="IEmailOutbox"/> so they are retried and sent after commit.
/// </summary>
public interface IEmailSender
{
    /// <exception cref="Jobs.PermanentJobFailureException">The message can never be delivered (e.g. invalid recipient).</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
