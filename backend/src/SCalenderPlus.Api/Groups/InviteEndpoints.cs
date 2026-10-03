using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Api.Groups;

/// <summary>
/// Group invites by email and by link (issue #36): create and list them below <c>/groups/{id}/invites</c>,
/// revoke with <c>DELETE /invites/{id}</c>, join with <c>POST /invites/accept</c>. Creating and accepting need a
/// verified email address (<c>403 email_not_verified</c>) and are rate limited per user. Rules live in
/// <see cref="GroupInviteService"/> / <c>Core.Groups.MembershipPolicy</c>.
/// </summary>
internal static class InviteEndpoints
{
    public static RouteGroupBuilder MapGroupInviteEndpoints(this RouteGroupBuilder groups)
    {
        groups.MapPost("/{id:guid}/invites", CreateAsync).WithName("CreateGroupInvite")
            .RequireVerifiedEmail().RequireRateLimiting(RateLimitingSetup.InviteCreate)
            .WithSummary("Invite by email or create an invite link (admins and owners)")
            .WithDescription("With email: a single-use invite sent to that address (en/de), usable only by an account whose verified email matches; a new invite replaces pending ones for the address. Without email: an invite link (role at most member, maxUses 1–1000, default 50) returned once in url. role: owners invite any role, admins up to member. expiresInDays: 1–30 (default 14 for email, 7 for links). Frozen groups: 409 group_frozen.");
        groups.MapGet("/{id:guid}/invites", ListAsync).WithName("ListGroupInvites")
            .WithSummary("Pending invites of a group (admins and owners, cursor-paginated)");
        return groups;
    }

    public static RouteGroupBuilder MapInviteEndpoints(this RouteGroupBuilder v1)
    {
        var invites = v1.MapGroup("/invites").WithTags(GroupEndpoints.Tag);
        invites.MapDelete("/{id:guid}", RevokeAsync).WithName("RevokeGroupInvite")
            .WithSummary("Revoke an invite (admins and owners; idempotent)")
            .WithDescription("Admins can revoke invites with roles up to member. Revoking an expired, used or revoked invite is a no-op.");
        invites.MapPost("/accept", AcceptAsync).WithName("AcceptGroupInvite")
            .RequireVerifiedEmail().RequireRateLimiting(RateLimitingSetup.InviteAccept)
            .WithSummary("Join a group with an invite token")
            .WithDescription("Needs a verified email address (403 email_not_verified); email invites only for the invited address (403 invite_email_mismatch). Unknown, expired, revoked and used-up tokens are 400 token_invalid alike. Members accepting again keep their role. Returns the group.");
        return invites;
    }

    private static async Task<Created<CreateInviteResponse>> CreateAsync(
        Guid id,
        CreateInviteRequest request,
        ClaimsPrincipal principal,
        GroupInviteService invites,
        CancellationToken cancellationToken)
    {
        var role = GroupRoles.TryParse(request.Role, out var parsed)
            ? parsed
            : throw Validation.Failed("role", "Use owner, admin, member or viewer.");
        var created = await invites.CreateAsync(
            principal.UserId(),
            id,
            new NewInvite(request.Email, role, request.ExpiresInDays, request.MaxUses),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"{ApiV1.BasePath}/groups/{id}/invites",
            new CreateInviteResponse(InviteResponse.From(created.Invite), created.Link?.AbsoluteUri));
    }

    private static async Task<Ok<InviteListResponse>> ListAsync(
        Guid id,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        ClaimsPrincipal principal,
        GroupInviteService invites,
        CancellationToken cancellationToken)
    {
        var page = await invites.ListPendingAsync(principal.UserId(), id, PageRequest.Parse(limit, cursor), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new InviteListResponse([.. page.Items.Select(InviteResponse.From)], page.NextCursor));
    }

    private static async Task<NoContent> RevokeAsync(Guid id, ClaimsPrincipal principal, GroupInviteService invites, CancellationToken cancellationToken)
    {
        await invites.RevokeAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<GroupResponse>> AcceptAsync(
        AcceptInviteRequest request,
        ClaimsPrincipal principal,
        GroupInviteService invites,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var group = GroupResponse.From(await invites.AcceptAsync(principal.UserId(), request.Token, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = ETags.Of(group);
        return TypedResults.Ok(group);
    }
}
