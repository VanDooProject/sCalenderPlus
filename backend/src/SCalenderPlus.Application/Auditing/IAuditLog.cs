namespace SCalenderPlus.Application.Auditing;

/// <summary>
/// Records audit events (<c>audit_events</c>, docs/architecture/data-model.md §8). <see cref="Record"/> stages
/// the event in the current unit of work, so it is written in the same transaction as the mutation it
/// describes (<c>IAppDbContext.SaveChangesAsync</c>) — no audit row without the change and vice versa.
/// Actor, IP address, user agent and correlation id come from <see cref="IActorContext"/>.
/// </summary>
public interface IAuditLog
{
    /// <param name="action">Dotted verb, e.g. <c>group.renamed</c>, <c>group.member.role_changed</c>.</param>
    /// <param name="resourceType">e.g. <c>group</c>, <c>calendar</c>, <c>event</c>.</param>
    /// <param name="resourceId">Id of the changed resource.</param>
    /// <param name="before">State before the change (null for creations); serialized to JSON with secrets redacted.</param>
    /// <param name="after">State after the change (null for deletions).</param>
    /// <param name="subjectId">Billing subject (user or group) whose plan sets the retention, when known.</param>
    void Record(string action, string resourceType, string resourceId, object? before, object? after, Guid? subjectId = null);
}

/// <summary>Who performs the current operation, and from where.</summary>
public interface IActorContext
{
    AuditActor Current { get; }
}

/// <param name="Kind">user (signed in), anonymous (unauthenticated request) or system (worker jobs, migrations).</param>
/// <param name="UserId">The signed-in user, if any.</param>
/// <param name="IpAddress">Client address (after trusted forwarded headers).</param>
/// <param name="UserAgent">Client user agent, truncated.</param>
/// <param name="CorrelationId">W3C trace id of the request or job: links the audit row to logs and traces.</param>
public sealed record AuditActor(AuditActorKind Kind, Guid? UserId, string? IpAddress, string? UserAgent, string? CorrelationId);

public enum AuditActorKind
{
    System,
    Anonymous,
    User,
}

/// <summary>Marks a property whose value must never appear in audit snapshots (written as <c>"[redacted]"</c>).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SensitiveAttribute : Attribute;
