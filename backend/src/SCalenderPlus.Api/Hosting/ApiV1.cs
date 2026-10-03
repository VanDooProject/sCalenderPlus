namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// The versioned public API (docs/architecture/api.md §1): every resource endpoint is mapped below
/// <see cref="BasePath"/> through <see cref="MapApiV1"/>. Feature modules add their endpoints to the group,
/// e.g. <c>group.MapGroupEndpoints()</c>; the group carries the conventions shared by all v1 operations.
/// </summary>
internal static class ApiV1
{
    public const string BasePath = "/api/v1";

    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);

        // Feature endpoints are mapped here (M1: auth, me, groups).

        return group;
    }
}
