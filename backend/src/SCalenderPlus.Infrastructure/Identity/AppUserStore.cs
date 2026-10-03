using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Identity;

/// <summary>
/// Identity's EF Core user store with two changes: <c>created_at</c>/<c>updated_at</c> are maintained, and
/// two-factor recovery codes are stored as SHA-256 hashes (the default store keeps them in clear text in
/// <c>user_tokens</c>). Codes are shown once when generated and are single-use (<see cref="RedeemCodeAsync"/>).
/// </summary>
internal sealed class AppUserStore(AppDbContext context, IClock clock, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<AppUser, AppDbContext, Guid>(context, describer)
{
    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string RecoveryCodeTokenName = "RecoveryCodes";

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

    public override Task ReplaceCodesAsync(AppUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        return base.ReplaceCodesAsync(user, recoveryCodes.Select(Hash), cancellationToken);
    }

    public override async Task<bool> RedeemCodeAsync(AppUser user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(code);
        cancellationToken.ThrowIfCancellationRequested();

        var merged = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var hashes = merged.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hash = Hash(code);

        // Constant-time comparison per entry; the list is short (10 codes).
        var match = hashes.FirstOrDefault(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(hash)));
        if (match is null)
        {
            return false;
        }

        hashes.Remove(match);
        await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, string.Join(';', hashes), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Codes are random (50 bits, shown once), so an unsalted hash is sufficient (like API tokens, data-model.md §5).
    /// Input is normalized: case, whitespace and the dash between the two halves (<c>ABCDE-12345</c>) don't matter.
    /// </summary>
    internal static string Hash(string code)
    {
        var normalized = new string([.. code.Where(c => !char.IsWhiteSpace(c) && c != '-')]).ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
