using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Identity;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        var idList = ids.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName, u.Locale, u.EmailConfirmed))
            .ToDictionaryAsync(u => u.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task BumpAclVersionAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return;
        }

        var idList = ids.Distinct().ToList();
        await db.Users.Where(u => idList.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.AclVersion, x => x.AclVersion + 1), cancellationToken).ConfigureAwait(false);
    }
}
