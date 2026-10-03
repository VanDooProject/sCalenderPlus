using System.Text;
using System.Text.Json.Serialization;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Events;

/// <summary>
/// One step of the engine's resolution (permissions.md §4.1/§4.2), top to bottom. <c>kind</c>: <c>share_link</c>,
/// <c>calendar_owner</c>, <c>grant_matched</c>, <c>group_role_default</c>, <c>calendar_result</c>,
/// <c>inherited_from_series</c>, <c>manager_floor</c>, <c>creator_floor</c>, <c>creator_floor_disabled</c>,
/// <c>creator_floor_lapsed</c>, <c>base_level</c>, <c>override_matched</c>, <c>no_matching_override</c>,
/// <c>override_applied</c>, <c>restrict_only</c>, <c>override_cap</c>, <c>link_ceiling</c>, <c>result</c>.
/// Only the members the kind uses are present.
/// </summary>
/// <param name="PrincipalName">Name of the user or group in <c>principal</c>.</param>
/// <param name="Role">The explained user's role in the owning group (<c>group_role_default</c>).</param>
/// <param name="Tier">Specificity of an override's principal: user 4, group 3, anonymous 2, everyone 1.</param>
/// <param name="Decisive">The override is on the winning tier.</param>
/// <param name="Applied">The restrict-only rule lowered the level.</param>
/// <param name="ResourceId">The series of an exception (<c>inherited_from_series</c>).</param>
public sealed record ExplainStepResponse(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PrincipalResponse? Principal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrincipalName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CalendarLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EventLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Tier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Decisive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Applied,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ResourceId)
{
    public static ExplainStepResponse From(ResolutionStep step, IReadOnlyDictionary<Guid, string> names)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(names);
        return new(
            KindName(step.Kind),
            step.Principal is { } principal ? PrincipalResponse.From(principal) : null,
            step.Principal?.Id is { } id ? names.GetValueOrDefault(id) : null,
            step.Role is { } role ? GroupRoles.Format(role) : null,
            step.CalendarLevel is { } calendarLevel ? PermissionLevels.Format(calendarLevel) : null,
            step.EventLevel is { } eventLevel ? PermissionLevels.Format(eventLevel) : null,
            step.Tier,
            step.Decisive,
            step.Applied,
            step.ResourceId);
    }

    /// <summary><see cref="ResolutionStepKind"/> in snake case (<c>GrantMatched</c> → <c>grant_matched</c>).</summary>
    public static string KindName(ResolutionStepKind kind)
    {
        var name = kind.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}

/// <summary>The user an explanation is about.</summary>
/// <param name="DisplayName">Absent for deleted accounts.</param>
public sealed record ExplainUserResponse(Guid Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName);

/// <summary>Why <c>user</c> has <c>level</c> on the event (permissions.md §4): the engine's steps.</summary>
/// <param name="CalendarLevel">The user's level on the event's calendar (§4.1).</param>
/// <param name="Level">The effective event level: <c>none</c> … <c>manage</c>.</param>
/// <param name="HiddenAsTransparent">The engine gives <c>free_busy</c>, but the event is transparent ("free"), so it is hidden (§2.1).</param>
public sealed record AccessExplanationResponse(
    Guid EventId,
    Guid CalendarId,
    ExplainUserResponse User,
    string CalendarLevel,
    string Level,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HiddenAsTransparent,
    IReadOnlyList<ExplainStepResponse> Steps)
{
    public static AccessExplanationResponse From(AccessExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        return new(
            explanation.Event.Id,
            explanation.Event.CalendarId,
            new ExplainUserResponse(explanation.UserId, explanation.UserName),
            PermissionLevels.Format(explanation.Access.CalendarLevel),
            PermissionLevels.Format(explanation.Level),
            explanation.HiddenAsTransparent ? true : null,
            [.. explanation.Access.Steps.Select(s => ExplainStepResponse.From(s, explanation.Names))]);
    }
}
