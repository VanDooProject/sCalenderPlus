using System.Security.Cryptography;
using System.Text.Json;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// Optimistic concurrency of mutable resources (docs/architecture/api.md §1): a strong <c>ETag</c> = hash of
/// the JSON representation (it changes exactly when the response does), and <c>If-Match</c> on
/// <c>PATCH</c>/<c>DELETE</c>: missing → <c>428 precondition_required</c>, stale → <c>412 precondition_failed</c>;
/// <c>*</c> matches any current representation.
/// </summary>
internal static class ETags
{
    public static string Of<T>(T representation)
    {
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(representation));
        return "\"" + Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "\"";
    }

    public static bool Matches(string ifMatch, string etag) =>
        ifMatch.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => candidate == "*" || string.Equals(candidate, etag, StringComparison.Ordinal));

    /// <summary>Throws the 428/412 problem unless <paramref name="ifMatch"/> matches <paramref name="currentETag"/>.</summary>
    /// <param name="source">Where the client gets the ETag, e.g. <c>GET /api/v1/groups/{id}</c>.</param>
    public static void Require(string? ifMatch, string currentETag, string source)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw new AppException(ErrorCodes.PreconditionRequired, $"Send If-Match with the ETag of {source}.");
        }

        if (!Matches(ifMatch, currentETag))
        {
            throw new AppException(ErrorCodes.PreconditionFailed, "The resource was changed meanwhile. Reload it and try again.");
        }
    }
}
