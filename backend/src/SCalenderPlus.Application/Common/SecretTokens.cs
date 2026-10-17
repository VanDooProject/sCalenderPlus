using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SCalenderPlus.Application.Common;

/// <summary>
/// Secrets that are credentials in URLs (feed tokens, share links; data-model.md §5): 32 random bytes as base64url
/// (43 characters), stored only as their SHA-256 hash and looked up by it.
/// </summary>
public static class SecretTokens
{
    /// <summary>Length of a generated token.</summary>
    public const int Length = 43;

    /// <summary>A new random token.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The stored form of <paramref name="token"/>.</summary>
    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>
    /// Whether <paramref name="token"/> can be one of ours (43 base64url characters): anything else is answered as
    /// unknown without a database lookup.
    /// </summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
