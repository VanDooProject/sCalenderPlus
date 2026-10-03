using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Application.Tests;

public sealed class PagingTests
{
    [Fact]
    public void Cursor_round_trips_the_key()
    {
        var key = Guid.CreateVersion7();

        var cursor = Cursors.Encode(key);

        Assert.Equal(22, cursor.Length);
        Assert.True(Cursors.TryDecode(cursor, out var decoded));
        Assert.Equal(key, decoded);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!")]
    public void Malformed_cursors_are_rejected(string cursor) => Assert.False(Cursors.TryDecode(cursor, out _));

    [Fact]
    public void Defaults_and_limits()
    {
        Assert.Equal(new PageRequest(PageRequest.DefaultLimit, null), PageRequest.Parse(null, null));
        Assert.Equal(200, PageRequest.Parse(200, "").Limit);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<AppException>(() => PageRequest.Parse(0, null)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<AppException>(() => PageRequest.Parse(201, null)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<AppException>(() => PageRequest.Parse(10, "x")).Code);
    }

    [Fact]
    public void A_page_has_a_cursor_only_when_more_items_follow()
    {
        var keys = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList();
        var request = new PageRequest(2, null);

        var full = request.ToPage(keys, k => k);
        var last = request.ToPage(keys.Take(2).ToList(), k => k);

        Assert.Equal(keys.Take(2), full.Items);
        Assert.Equal(Cursors.Encode(keys[1]), full.NextCursor);
        Assert.Null(last.NextCursor);
    }
}
