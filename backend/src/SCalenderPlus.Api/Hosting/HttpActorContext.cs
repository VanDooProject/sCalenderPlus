using System.Diagnostics;
using System.Security.Claims;
using SCalenderPlus.Application.Auditing;

namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// The actor of the current HTTP request: the signed-in user (<see cref="ClaimTypes.NameIdentifier"/>, set by
/// cookie/token authentication from M1 auth on) or anonymous, with the client address (forwarded headers from
/// trusted proxies already applied), user agent and trace id.
/// </summary>
internal sealed class HttpActorContext(IHttpContextAccessor accessor) : IActorContext
{
    public AuditActor Current
    {
        get
        {
            var context = accessor.HttpContext;
            var correlationId = Activity.Current?.TraceId.ToHexString();
            if (context is null)
            {
                return new AuditActor(AuditActorKind.System, null, null, null, correlationId);
            }

            var userId = context.User.Identity?.IsAuthenticated == true
                && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                    ? id
                    : (Guid?)null;
            return new AuditActor(
                userId is null ? AuditActorKind.Anonymous : AuditActorKind.User,
                userId,
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                correlationId ?? context.TraceIdentifier);
        }
    }
}
