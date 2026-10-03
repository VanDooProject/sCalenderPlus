using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Api.Groups;

/// <summary>
/// Membership management (issue #35): <c>/api/v1/groups/{id}/members</c> and the billing-owner transfer. Rules
/// live in <see cref="GroupMembershipService"/> / <c>Core.Groups.MembershipPolicy</c>. Changing or removing a
/// membership requires <c>If-Match</c> with the member's <c>etag</c> from the member list (or <c>*</c>).
/// </summary>
internal static class MemberEndpoints
{
    private const string ETagSource = "the member in GET /api/v1/groups/{id}/members";

    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder groups)
    {
        groups.MapGet("/{id:guid}/members", ListAsync).WithName("ListGroupMembers")
            .WithSummary("Members of a group (cursor-paginated)")
            .WithDescription("Every member sees the list unless memberListVisibility is members_and_above (viewers: 403). Emails are only shown to admins and owners.");
        groups.MapPatch("/{id:guid}/members/{userId:guid}", ChangeRoleAsync).WithName("ChangeGroupMemberRole")
            .WithSummary("Change a member's role (requires If-Match)")
            .WithDescription("Owners assign any role; admins manage members and viewers only and assign at most member (only owners promote to admin or owner); everyone may lower their own role. The last owner cannot be demoted (409 last_owner), the billing owner only after transferring billing (409 billing_owner_transfer_required); frozen groups refuse role changes (409 group_frozen). revokeEventShares (default true): on demotion also revoke the member's individual event shares on the group's calendars.");
        groups.MapDelete("/{id:guid}/members/{userId:guid}", RemoveAsync).WithName("RemoveGroupMember")
            .WithSummary("Remove a member, or leave the group with your own user id (requires If-Match)")
            .WithDescription("Owners remove anyone, admins members and viewers; everyone may leave. Last-owner and billing-owner protection as for role changes. revokeEventShares (default true): also revoke the member's individual event shares on the group's calendars.");
        groups.MapPost("/{id:guid}/transfer", TransferBillingAsync).WithName("TransferGroupBilling")
            .WithSummary("Make another owner the billing owner (billing owner only)")
            .WithDescription("The recipient must be a member with role owner (409 billing_owner_must_be_owner); their plan governs the group from now on.");
        return groups;
    }

    private static async Task<Ok<MemberListResponse>> ListAsync(
        Guid id,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        ClaimsPrincipal principal,
        GroupMembershipService members,
        CancellationToken cancellationToken)
    {
        var page = await members.ListAsync(principal.UserId(), id, PageRequest.Parse(limit, cursor), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new MemberListResponse([.. page.Items.Select(MemberResponse.From)], page.NextCursor));
    }

    private static async Task<Ok<MemberResponse>> ChangeRoleAsync(
        Guid id,
        Guid userId,
        ChangeMemberRoleRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromQuery] bool? revokeEventShares,
        ClaimsPrincipal principal,
        GroupMembershipService members,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var role = GroupRoles.TryParse(request.Role, out var parsed)
            ? parsed
            : throw Validation.Failed("role", "Use owner, admin, member or viewer.");
        var member = MemberResponse.From(await members.ChangeRoleAsync(
            principal.UserId(),
            id,
            userId,
            role,
            revokeEventShares ?? true,
            current => ETags.Require(ifMatch, MemberResponse.From(current).Etag, ETagSource),
            cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = member.Etag;
        return TypedResults.Ok(member);
    }

    private static async Task<NoContent> RemoveAsync(
        Guid id,
        Guid userId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromQuery] bool? revokeEventShares,
        ClaimsPrincipal principal,
        GroupMembershipService members,
        CancellationToken cancellationToken)
    {
        await members.RemoveAsync(
            principal.UserId(),
            id,
            userId,
            revokeEventShares ?? true,
            current => ETags.Require(ifMatch, MemberResponse.From(current).Etag, ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<GroupResponse>> TransferBillingAsync(
        Guid id,
        TransferBillingRequest request,
        ClaimsPrincipal principal,
        GroupMembershipService members,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var group = GroupResponse.From(await members.TransferBillingAsync(principal.UserId(), id, request.UserId, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = ETags.Of(group);
        return TypedResults.Ok(group);
    }
}
