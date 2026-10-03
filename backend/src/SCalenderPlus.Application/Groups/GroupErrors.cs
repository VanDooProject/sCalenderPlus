using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

internal static class GroupErrors
{
    /// <summary>Unknown group or not a member: indistinguishable (no existence leaks).</summary>
    public static AppException GroupNotFound() => new(ErrorCodes.NotFound, "Group not found.");

    public static AppException InsufficientRole(GroupRole required, GroupRole actual) =>
        new(ErrorCodes.InsufficientPermission, $"This needs the group role {GroupRoles.Format(required)}.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["required"] = GroupRoles.Format(required),
            ["actual"] = GroupRoles.Format(actual),
        });

    public static AppException Changed() =>
        new(ErrorCodes.PreconditionFailed, "The group was changed meanwhile. Reload it and try again.");
}
