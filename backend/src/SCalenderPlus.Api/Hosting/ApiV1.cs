using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Groups;

namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// The versioned public API (docs/architecture/api.md §1): every resource endpoint is mapped below
/// <see cref="BasePath"/> through <see cref="MapApiV1"/>. Feature modules add their endpoints to the group,
/// e.g. <c>group.MapGroupEndpoints()</c>; the group carries the conventions shared by all v1 operations:
/// authentication is required unless an endpoint opts out with <c>AllowAnonymous()</c> (secure by default).
/// </summary>
internal static class ApiV1
{
    public const string BasePath = "/api/v1";

    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization();

        group.MapAuthEndpoints();
        group.MapMeEndpoints();
        group.MapGroupEndpoints();
        group.MapInviteEndpoints();

        return group;
    }
}
