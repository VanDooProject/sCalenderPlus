using System.Net;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Auditing;

internal sealed class AuditLog(AppDbContext db, IActorContext actors, IClock clock) : IAuditLog
{
    public void Record(string action, string resourceType, string resourceId, object? before, object? after, Guid? subjectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var actor = actors.Current;
        db.Set<AuditEvent>().Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            At = clock.GetCurrentInstant(),
            ActorKind = actor.Kind.ToString().ToLowerInvariant(),
            ActorUserId = actor.UserId,
            SubjectId = subjectId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            Before = AuditSnapshot.Serialize(before),
            After = AuditSnapshot.Serialize(after),
            Ip = IPAddress.TryParse(actor.IpAddress, out var ip) ? ip : null,
            UserAgent = actor.UserAgent is { Length: > AuditEventConfiguration.UserAgentMaxLength } ua
                ? ua[..AuditEventConfiguration.UserAgentMaxLength]
                : actor.UserAgent,
            CorrelationId = actor.CorrelationId,
        });
    }
}
