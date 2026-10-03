using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Identity;

/// <summary>Identity's EF Core user store that also maintains <c>created_at</c>/<c>updated_at</c>.</summary>
internal sealed class AppUserStore(AppDbContext context, IClock clock, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<AppUser, AppDbContext, Guid>(context, describer)
{
    public override Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.CreatedAt = user.UpdatedAt = clock.GetCurrentInstant();
        return base.CreateAsync(user, cancellationToken);
    }

    public override Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.UpdatedAt = clock.GetCurrentInstant();
        return base.UpdateAsync(user, cancellationToken);
    }
}
