using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NodaTime;

namespace SCalenderPlus.Application.Feeds;

/// <summary>A rendered feed: the UTF-8 body, its ETag and when it was rendered (the <c>Last-Modified</c> of that ETag).</summary>
public sealed record RenderedFeed(byte[] Body, string ETag, Instant RenderedAt);

/// <summary>
/// The in-process cache of rendered feeds (ical-caldav.md §1.4): keyed by token hash + ETag, absolute expiry
/// (<see cref="FeedOptions.CacheDuration"/>), size-limited by body bytes (<see cref="FeedOptions.CacheSizeMegabytes"/>).
/// It absorbs burst polling only — the token is checked and the ETag computed on every request before it is asked,
/// so a revoked token never reaches a cached render. Singleton, separate from any other memory cache.
/// </summary>
public sealed class FeedCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _duration;

    public FeedCache(IOptions<FeedOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheSizeMegabytes * 1024L * 1024L });
        _duration = options.Value.CacheDuration;
    }

    public RenderedFeed? Get(string key, string etag) =>
        _cache.TryGetValue(Key(key, etag), out RenderedFeed? feed) ? feed : null;

    public void Set(string key, RenderedFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        _cache.Set(Key(key, feed.ETag), feed, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _duration,
            Size = Math.Max(1, feed.Body.Length),
        });
    }

    public void Dispose() => _cache.Dispose();

    private static string Key(string key, string etag) => key + "|" + etag;
}
