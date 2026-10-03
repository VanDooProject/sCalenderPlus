namespace SCalenderPlus.Core.Groups;

/// <summary>
/// Who may see a group's member list (permissions.md §6.1: viewers see it unless the group hides it from them).
/// Members, admins and owners always see it. API values: <c>all_members</c>, <c>members_and_above</c>.
/// </summary>
public enum MemberListVisibility : short
{
    /// <summary>Every member including viewers (default).</summary>
    AllMembers = 0,

    /// <summary>Role <c>member</c> and above; viewers only see the group itself.</summary>
    MembersAndAbove = 1,
}

public static class MemberListVisibilities
{
    public static string Format(MemberListVisibility visibility) => visibility switch
    {
        MemberListVisibility.AllMembers => "all_members",
        MemberListVisibility.MembersAndAbove => "members_and_above",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Unknown member list visibility."),
    };

    public static bool TryParse(string? value, out MemberListVisibility visibility)
    {
        foreach (var candidate in new[] { MemberListVisibility.AllMembers, MemberListVisibility.MembersAndAbove })
        {
            if (string.Equals(value, Format(candidate), StringComparison.Ordinal))
            {
                visibility = candidate;
                return true;
            }
        }

        visibility = default;
        return false;
    }
}
