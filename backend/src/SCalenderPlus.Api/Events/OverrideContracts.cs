using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Events;

/// <summary>Who an override (or an explainer step) names.</summary>
/// <param name="Type"><c>user</c>, <c>group</c>, <c>anonymous</c> (share-link holders) or <c>everyone</c> (the calendar's audience).</param>
/// <param name="Id">User or group id; absent for <c>anonymous</c>/<c>everyone</c>.</param>
/// <param name="MinRole">Groups: the lowest matching role (<c>viewer</c> = all members).</param>
public sealed record PrincipalResponse(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MinRole)
{
    public static PrincipalResponse From(Principal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new(PrincipalTypes.Format(principal.Type), principal.Id, principal.MinRole is { } role ? GroupRoles.Format(role) : null);
    }
}

/// <summary>An event permission override: <c>principal → level</c> (permissions.md §4.2).</summary>
/// <param name="PrincipalName">Display name of the user or name of the group; absent for <c>anonymous</c>/<c>everyone</c> and deleted accounts.</param>
/// <param name="Level"><c>none</c>, <c>free_busy</c>, <c>read</c> or <c>edit</c>.</param>
/// <param name="CreatedBy">Who set the entry at its current level.</param>
public sealed record OverrideResponse(
    PrincipalResponse Principal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrincipalName,
    string Level,
    Guid CreatedBy,
    DateTimeOffset CreatedAt)
{
    public static OverrideResponse From(OverrideView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var entry = view.Entry;
        return new(PrincipalResponse.From(entry.Principal), view.PrincipalName, PermissionLevels.Format(entry.Level), entry.CreatedBy, entry.CreatedAt.ToDateTimeOffset());
    }
}

/// <summary>The overrides of an event (ordered: users, groups, anonymous, everyone).</summary>
/// <param name="Etag">Send as <c>If-Match</c> to replace them (also the <c>ETag</c> header).</param>
public sealed record EventOverridesResponse(Guid EventId, IReadOnlyList<OverrideResponse> Items, string Etag)
{
    public static EventOverridesResponse From(EventOverridesView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var items = view.Overrides.Select(OverrideResponse.From).ToList();
        return new(view.Event.Event.Id, items, ETagOf(view.Event.Event.Id, items));
    }

    /// <summary>Changes exactly when the set does (names and authors are left out).</summary>
    public static string ETagOf(Guid eventId, IEnumerable<OverrideResponse> items) =>
        ETags.Of(new { eventId, Items = items.Select(i => new { i.Principal, i.Level }) });
}

/// <summary>Who an override names.</summary>
public sealed class PrincipalRequest
{
    /// <summary><c>user</c>, <c>group</c>, <c>anonymous</c> or <c>everyone</c>.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// <c>user</c>: someone who sees the calendar or shares a group with you. <c>group</c>: one of your groups, the
    /// owning group, or a group with a grant on the calendar. Absent for <c>anonymous</c>/<c>everyone</c>.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>Groups only: the lowest matching role, default <c>viewer</c> (all members).</summary>
    public string? MinRole { get; init; }

    public Principal Parse(string field)
    {
        switch (Type)
        {
            case "user" or "group" when Id is null || Id == Guid.Empty:
                throw Validation.Failed(field + ".id", "Users and groups are named by id.");
            case "user" when MinRole is null:
                return Principal.User(Id!.Value);
            case "group":
                var role = GroupRole.Viewer;
                if (MinRole is not null && !GroupRoles.TryParse(MinRole, out role))
                {
                    throw Validation.Failed(field + ".minRole", "Use viewer, member, admin or owner.");
                }

                return Principal.Group(Id!.Value, role);
            case "anonymous" or "everyone" when Id is null && MinRole is null:
                return Type == "anonymous" ? Principal.Anonymous : Principal.Everyone;
            case "user" or "anonymous" or "everyone":
                throw Validation.Failed(field, "Only groups have a minimum role; anonymous and everyone have no id.");
            default:
                throw Validation.Failed(field + ".type", "Use user, group, anonymous or everyone.");
        }
    }
}

public sealed class OverrideRequest
{
    [Required]
    public PrincipalRequest Principal { get; init; } = new();

    /// <summary><c>none</c>, <c>free_busy</c>, <c>read</c> or <c>edit</c> (<c>manage</c> only comes from floors: 422 <c>override_invalid</c>).</summary>
    [Required]
    public string Level { get; init; } = string.Empty;
}

/// <summary>The complete new set of an event's overrides (replaces the stored set atomically; an empty list removes all).</summary>
public sealed class ReplaceOverridesRequest
{
    /// <summary>At most 200 entries (plan limits allow fewer).</summary>
    public const int MaxEntries = 200;

    [Required]
    public IReadOnlyList<OverrideRequest>? Overrides { get; init; }

    public IReadOnlyList<EventOverride> Parse()
    {
        var entries = Overrides ?? throw Validation.Failed("overrides", "Give the complete list of entries (empty removes all).");
        if (entries.Count > MaxEntries)
        {
            throw Validation.Failed("overrides", $"At most {MaxEntries} entries.");
        }

        return [.. entries.Select((entry, i) =>
        {
            var field = $"overrides[{i}]";
            var principal = (entry ?? throw Validation.Failed(field, "An entry is required.")).Principal?.Parse(field + ".principal")
                ?? throw Validation.Failed(field + ".principal", "The principal is required.");
            return PermissionLevels.TryParse(entry.Level, out EventLevel level)
                ? new EventOverride(principal, level)
                : throw Validation.Failed(field + ".level", "Use none, free_busy, read or edit.");
        })];
    }
}
