using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Application.Entitlements;

namespace SCalenderPlus.Api.Entitlements;

/// <summary>
/// <c>GET /api/v1/me/entitlements</c>: the signed-in user's plan as a billing owner (docs/product/plans.md) — the
/// plan that governs their own calendars and the groups they bill — with its limits and the end of the reverse
/// trial. The server enforces the limits (<see cref="IEntitlementService"/>); this is for showing them.
/// </summary>
internal static class EntitlementEndpoints
{
    public static RouteGroupBuilder MapEntitlementEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/me/entitlements", GetAsync).WithTags(MeEndpoints.Tag).WithName("GetMyEntitlements")
            .WithSummary("My plan and its limits")
            .WithDescription("plan: free, pro, team or selfhost. trialEndsAt: end of the reverse trial while it runs (plan pro, then free), else null. limits: null = unlimited. The plan of a resource's owner governs it (a group's billing owner, a calendar's owner), so limits of shared resources may differ from mine.");
        return v1;
    }

    private static async Task<Ok<EntitlementsResponse>> GetAsync(ClaimsPrincipal principal, IEntitlementService entitlements, CancellationToken cancellationToken) =>
        TypedResults.Ok(EntitlementsResponse.From(await entitlements.GetAsync(principal.UserId(), cancellationToken).ConfigureAwait(false)));
}
