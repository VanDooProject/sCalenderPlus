using System.Buffers.Text;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Application.Common;

/// <summary>One page of a cursor-paginated list (docs/architecture/api.md §1): <c>{ items, nextCursor }</c>.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Requested page: at most <see cref="Limit"/> items after the item with key <see cref="After"/> (keyset
/// pagination on a UUID key). Cursors are opaque to clients: base64url of the last key of the previous page.
/// </summary>
public readonly record struct PageRequest(int Limit, Guid? After)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static PageRequest First { get; } = new(DefaultLimit, null);

    /// <summary>Validates the query values <c>limit</c> (1 … 200, default 50) and <c>cursor</c>.</summary>
    /// <exception cref="AppException"><c>validation_failed</c> with the offending parameter in <c>errors</c>.</exception>
    public static PageRequest Parse(int? limit, string? cursor)
    {
        if (limit is < 1 or > MaxLimit)
        {
            throw Validation.Failed("limit", $"Use a limit between 1 and {MaxLimit}.");
        }

        Guid? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = Cursors.TryDecode(cursor, out var key) ? key : throw Validation.Failed("cursor", "The cursor is invalid; use nextCursor of the previous page.");
        }

        return new PageRequest(limit ?? DefaultLimit, after);
    }

    /// <summary>Cuts a query result of up to <see cref="Limit"/> + 1 items into a page.</summary>
    public Page<T> ToPage<T>(IReadOnlyList<T> itemsPlusOne, Func<T, Guid> key)
    {
        ArgumentNullException.ThrowIfNull(itemsPlusOne);
        ArgumentNullException.ThrowIfNull(key);
        if (itemsPlusOne.Count <= Limit)
        {
            return new Page<T>(itemsPlusOne, null);
        }

        var items = itemsPlusOne.Take(Limit).ToList();
        return new Page<T>(items, Cursors.Encode(key(items[^1])));
    }
}

public static class Cursors
{
    public static string Encode(Guid key) => Base64Url.EncodeToString(key.ToByteArray(bigEndian: true));

    public static bool TryDecode(string cursor, out Guid key)
    {
        key = Guid.Empty;
        Span<byte> bytes = stackalloc byte[16];
        if (cursor is null || cursor.Length != 22 || !Base64Url.IsValid(cursor) || !Base64Url.TryDecodeFromChars(cursor, bytes, out var written) || written != 16)
        {
            return false;
        }

        key = new Guid(bytes, bigEndian: true);
        return true;
    }
}
