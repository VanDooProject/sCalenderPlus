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
/// <c>/api/v1/groups</c> (issue #34, docs/architecture/api.md §4): the signed-in user's groups with their role.
/// Rules live in <see cref="GroupService"/> / <c>Core.Groups.GroupPolicy</c>; non-members get 404 for every
/// group. A group has an <c>ETag</c>; <c>PATCH</c> and <c>DELETE</c> require <c>If-Match</c> (like <c>/me</c>).
/// </summary>
internal static class GroupEndpoints
{
    public const string Tag = "Groups";
    private const string ETagSource = "GET /api/v1/groups/{id}";

    public static RouteGroupBuilder MapGroupEndpoints(this RouteGroupBuilder v1)
    {
        var groups = v1.MapGroup("/groups").WithTags(Tag);

        groups.MapGet(string.Empty, ListAsync).WithName("ListGroups")
            .WithSummary("My groups with my role (cursor-paginated)")
            .WithDescription("Ordered by creation. limit: 1–200 (default 50); cursor: nextCursor of the previous page.");
        groups.MapPost(string.Empty, CreateAsync).WithName("CreateGroup")
            .WithSummary("Create a group (the caller becomes owner and billing owner)");
        groups.MapGet("/{id:guid}", GetAsync).WithName("GetGroup")
            .WithSummary("A group I am a member of (with ETag)")
            .WithDescription("404 for groups the caller is not a member of (no existence leaks).");
        groups.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateGroup")
            .Accepts<UpdateGroupRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Rename or change settings (admins and owners; JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged; an empty description removes it. Members and viewers: 403 insufficient_permission. If-Match: the ETag of GET /groups/{id} (or *).");
        groups.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteGroup")
            .WithSummary("Delete the group with its memberships and invites (owners only, requires If-Match)");
        groups.MapMemberEndpoints();
        groups.MapGroupInviteEndpoints();

        return groups;
    }

    private static async Task<Ok<GroupListResponse>> ListAsync(
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        ClaimsPrincipal principal,
        GroupService groups,
        CancellationToken cancellationToken)
    {
        var page = await groups.ListMineAsync(principal.UserId(), PageRequest.Parse(limit, cursor), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new GroupListResponse([.. page.Items.Select(GroupResponse.From)], page.NextCursor));
    }

    private static async Task<Created<GroupResponse>> CreateAsync(
        CreateGroupRequest request,
        ClaimsPrincipal principal,
        GroupService groups,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var view = await groups.CreateAsync(principal.UserId(), request.Name, request.Description, cancellationToken).ConfigureAwait(false);
        var group = GroupResponse.From(view);
        response.Headers.ETag = ETags.Of(group);
        return TypedResults.Created($"{ApiV1.BasePath}/groups/{group.Id}", group);
    }

    private static async Task<Ok<GroupResponse>> GetAsync(Guid id, ClaimsPrincipal principal, GroupService groups, HttpResponse response, CancellationToken cancellationToken)
    {
        var group = GroupResponse.From(await groups.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = ETags.Of(group);
        return TypedResults.Ok(group);
    }

    private static async Task<Ok<GroupResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateGroupRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        GroupService groups,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        MemberListVisibility? visibility = null;
        if (request.MemberListVisibility is not null)
        {
            visibility = MemberListVisibilities.TryParse(request.MemberListVisibility, out var parsed)
                ? parsed
                : throw Validation.Failed("memberListVisibility", "Use all_members or members_and_above.");
        }

        var view = await groups.UpdateAsync(
            principal.UserId(),
            id,
            new GroupChanges(request.Name, request.Description, visibility),
            current => ETags.Require(ifMatch, ETags.Of(GroupResponse.From(current)), ETagSource),
            cancellationToken).ConfigureAwait(false);
        var group = GroupResponse.From(view);
        response.Headers.ETag = ETags.Of(group);
        return TypedResults.Ok(group);
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        GroupService groups,
        CancellationToken cancellationToken)
    {
        await groups.DeleteAsync(
            principal.UserId(),
            id,
            current => ETags.Require(ifMatch, ETags.Of(GroupResponse.From(current)), ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
