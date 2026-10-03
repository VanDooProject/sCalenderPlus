using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>RFC 6238 TOTP as authenticator apps compute it (HMAC-SHA1, 30-second steps, 6 digits, base32 secret).</summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Compute(string base32Secret, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(base32Secret);
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);

#pragma warning disable CA5350 // RFC 6238 authenticator apps use HMAC-SHA1.
        var hash = HMACSHA1.HashData(Decode(base32Secret), message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Decode(string base32)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant().Where(c => c != ' '))
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
