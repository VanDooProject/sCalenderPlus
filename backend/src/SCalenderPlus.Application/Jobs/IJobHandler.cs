using System.Text.Json;

namespace SCalenderPlus.Application.Jobs;

/// <summary>Processes jobs of one <see cref="Type"/>. Registered in DI; resolved per job in its own scope.</summary>
public interface IJobHandler
{
    string Type { get; }

    /// <summary>
    /// Runs one attempt. Return normally on success; throw to retry with backoff, or throw
    /// <see cref="PermanentJobFailureException"/> to dead-letter immediately. The token is cancelled on
    /// shutdown and when the job's lease is lost (another worker may then run it).
    /// </summary>
    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>One attempt of a job. <see cref="Attempt"/> starts at 1.</summary>
public sealed record JobContext(Guid JobId, string Type, int Attempt, int MaxAttempts, string Payload)
{
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public TPayload GetPayload<TPayload>() =>
        JsonSerializer.Deserialize<TPayload>(Payload, PayloadJson)
        ?? throw new PermanentJobFailureException($"Job {JobId} ({Type}) has an empty payload.");
}

/// <summary>Base class for handlers with a JSON payload of type <typeparamref name="TPayload"/>.</summary>
public abstract class JobHandler<TPayload> : IJobHandler
{
    public abstract string Type { get; }

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        TPayload payload;
        try
        {
            payload = context.GetPayload<TPayload>();
        }
        catch (JsonException ex)
        {
            throw new PermanentJobFailureException($"Job {context.JobId} ({context.Type}) has an invalid payload.", ex);
        }

        return HandleAsync(payload, context, cancellationToken);
    }

    protected abstract Task HandleAsync(TPayload payload, JobContext context, CancellationToken cancellationToken);
}

/// <summary>The job can never succeed (invalid payload, rejected recipient): dead-letter without retries.</summary>
public sealed class PermanentJobFailureException : Exception
{
    public PermanentJobFailureException()
    {
    }

    public PermanentJobFailureException(string message)
        : base(message)
    {
    }

    public PermanentJobFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
