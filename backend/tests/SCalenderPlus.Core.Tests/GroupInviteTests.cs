using NodaTime;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Tests;

public sealed class GroupInviteTests
{
    private static readonly Instant _now = Instant.FromUtc(2026, 10, 3, 12, 0);

    [Fact]
    public void Pending_means_not_revoked_not_expired_and_uses_left()
    {
        var invite = new GroupInvite { MaxUses = 2, Uses = 1, ExpiresAt = _now + Duration.FromDays(1) };

        Assert.True(invite.IsPending(_now));
        Assert.True(invite.IsLink);
        Assert.False(invite.IsPending(_now + Duration.FromDays(1)));
        Assert.False(new GroupInvite { MaxUses = 2, Uses = 2, ExpiresAt = invite.ExpiresAt }.IsPending(_now));
        invite.RevokedAt = _now;
        Assert.False(invite.IsPending(_now));
    }
}
