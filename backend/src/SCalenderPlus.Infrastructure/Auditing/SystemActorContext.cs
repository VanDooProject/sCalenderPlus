using System.Diagnostics;
using SCalenderPlus.Application.Auditing;

namespace SCalenderPlus.Infrastructure.Auditing;

/// <summary>Default actor outside HTTP requests (worker jobs, CLI): the system, correlated by the current trace.</summary>
internal sealed class SystemActorContext : IActorContext
{
    public AuditActor Current => new(AuditActorKind.System, null, null, null, Activity.Current?.TraceId.ToHexString());
}
