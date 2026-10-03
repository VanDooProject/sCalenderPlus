using SCalenderPlus.Application.Jobs;

namespace SCalenderPlus.Application.Email;

/// <summary>Queues emails as <c>email.send</c> jobs in the current unit of work (sent after commit, with retries).</summary>
public interface IEmailOutbox
{
    Guid Queue(EmailMessage message);
}

internal sealed class EmailOutbox(IJobScheduler jobs) : IEmailOutbox
{
    /// <summary>SMTP outages are usually short; with the default backoff 8 attempts span about two hours.</summary>
    public const int MaxAttempts = 8;

    public Guid Queue(EmailMessage message) =>
        jobs.Enqueue(SendEmailJobHandler.JobType, message, new EnqueueOptions(MaxAttempts: MaxAttempts));
}

/// <summary>Worker side of <see cref="IEmailOutbox"/>: delivers the queued message.</summary>
internal sealed class SendEmailJobHandler(IEmailSender sender) : JobHandler<EmailMessage>
{
    public const string JobType = "email.send";

    public override string Type => JobType;

    protected override Task HandleAsync(EmailMessage payload, JobContext context, CancellationToken cancellationToken) =>
        sender.SendAsync(payload, cancellationToken);
}
