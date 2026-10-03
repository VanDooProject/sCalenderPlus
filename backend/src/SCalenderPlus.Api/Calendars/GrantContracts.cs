using System.ComponentModel.DataAnnotations;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Calendars;

/// <summary>Who a grant names: a user, or the members of a group with at least <see cref="MinRole"/>.</summary>
/// <param name="Type"><c>user</c> or <c>group</c>.</param>
/// <param name="MinRole">Groups: the lowest matching role (<c>viewer</c> = all members); null for users.</param>
public sealed record GrantPrincipalResponse(string Type, Guid Id, string? MinRole);

/// <summary>A calendar grant.</summary>
/// <param name="PrincipalName">Display name of the user, or name of the group.</param>
/// <param name="Level"><c>free_busy</c>, <c>read</c>, <c>contribute</c>, <c>edit</c> or <c>manage</c>.</param>
/// <param name="Etag">Send as <c>If-Match</c> to change or remove this grant.</param>
public sealed record GrantResponse(
    Guid Id,
    Guid CalendarId,
    GrantPrincipalResponse Principal,
    string PrincipalName,
    string Level,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Etag)
{
    public static GrantResponse From(GrantView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var g = view.Grant;
        return new GrantResponse(
            g.Id,
            g.CalendarId,
            new GrantPrincipalResponse(
                g.PrincipalType == PrincipalType.Group ? "group" : "user",
                g.PrincipalId,
                g.MinRole is { } role ? GroupRoles.Format(role) : null),
            view.PrincipalName,
            PermissionLevels.Format(g.Level),
            g.CreatedBy,
            g.CreatedAt.ToDateTimeOffset(),
            g.UpdatedAt.ToDateTimeOffset(),
            ETagOf(g));
    }

    /// <summary>Changes exactly when the grant does (principal names are left out: they change elsewhere).</summary>
    public static string ETagOf(CalendarGrantEntry grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return ETags.Of(new { grant.Id, grant.PrincipalType, grant.PrincipalId, grant.MinRole, grant.Level, UpdatedAt = grant.UpdatedAt.ToDateTimeOffset() });
    }
}

/// <summary>One page of a calendar's grants (ordered by creation).</summary>
public sealed record GrantListResponse(IReadOnlyList<GrantResponse> Items, string? NextCursor);

public sealed class GrantPrincipalRequest
{
    /// <summary><c>user</c> or <c>group</c>.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// The user (someone who shares a group with you or already sees the calendar) or the group (one of yours,
    /// or one that already has a grant on the calendar).
    /// </summary>
    [Required]
    public Guid Id { get; init; }

    /// <summary>Groups only: the lowest matching role, default <c>viewer</c> (all members).</summary>
    public string? MinRole { get; init; }

    public Principal Parse()
    {
        switch (Type)
        {
            case "user" when MinRole is null:
                return Principal.User(NotEmpty(Id));
            case "user":
                throw Validation.Failed("principal.minRole", "Only group principals have a minimum role.");
            case "group":
                var role = GroupRole.Viewer;
                if (MinRole is not null && !GroupRoles.TryParse(MinRole, out role))
                {
                    throw Validation.Failed("principal.minRole", "Use viewer, member, admin or owner.");
                }

                return Principal.Group(NotEmpty(Id), role);
            default:
                throw Validation.Failed("principal.type", "Use user or group.");
        }
    }

    private static Guid NotEmpty(Guid id) => id == Guid.Empty ? throw Validation.Failed("principal.id", "The id is required.") : id;
}

public sealed class CreateGrantRequest
{
    [Required]
    public GrantPrincipalRequest Principal { get; init; } = new();

    /// <summary><c>free_busy</c>, <c>read</c>, <c>contribute</c>, <c>edit</c> or <c>manage</c> (never <c>owner</c>, never above your own level).</summary>
    [Required]
    public string Level { get; init; } = string.Empty;
}

/// <summary>JSON Merge Patch of a grant: its level (the principal is fixed; remove and add to change it).</summary>
public sealed class UpdateGrantRequest
{
    /// <summary><c>free_busy</c>, <c>read</c>, <c>contribute</c>, <c>edit</c> or <c>manage</c>.</summary>
    public string? Level { get; init; }
}

internal static class GrantLevels
{
    public static CalendarLevel Parse(string? value) =>
        PermissionLevels.TryParse(value, out CalendarLevel level) && level is >= CalendarLevel.FreeBusy and <= CalendarLevel.Owner
            ? level
            : throw Validation.Failed("level", "Use free_busy, read, contribute, edit or manage.");
}
