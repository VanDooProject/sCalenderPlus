using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NodaTime;

namespace SCalenderPlus.Application.Feeds;

/// <summary>A rendered feed: the UTF-8 body, its entity tag (the hash of the body) and since when this replica has served that body.</summary>
public sealed record RenderedFeed(byte[] Body, string ETag, Instant LastModified);

/// <summary>
/// The in-process cache of rendered feeds (ical-caldav.md §1.4), two kinds of entries per token:
/// <list type="bullet">
/// <item>the render, keyed by token hash + feed state (the internal hash of every render input): absolute expiry
/// (<see cref="FeedOptions.CacheDuration"/>), size-limited by body bytes (<see cref="FeedOptions.CacheSizeMegabytes"/>)
/// — it absorbs burst polling and lets an unchanged state answer 304 without loading events;</item>
/// <item>the body ETag last served for the token and since when (a few bytes, kept for <see cref="BodyHistory"/>):
/// a re-render after a change the viewer cannot see produces the same body, and so keeps its <c>Last-Modified</c>.</item>
/// </list>
/// The token is checked and the state computed on every request before the cache is asked, so a revoked token never
/// reaches a cached render. Singleton, separate from any other memory cache.
/// </summary>
public sealed class FeedCache : IDisposable
{
    /// <summary>How long the body ETag of a token is remembered after its last render.</summary>
    public static readonly TimeSpan BodyHistory = TimeSpan.FromDays(7);

    private const int BodyEntrySize = 128;

    private readonly MemoryCache _cache;
    private readonly TimeSpan _duration;

    public FeedCache(IOptions<FeedOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheSizeMegabytes * 1024L * 1024L });
        _duration = options.Value.CacheDuration;
    }

    public RenderedFeed? Get(string key, string state) =>
        _cache.TryGetValue(RenderKey(key, state), out RenderedFeed? feed) ? feed : null;

    /// <summary>Since when the token's feed has had the body <paramref name="etag"/>; null if it had another body last (or none is remembered).</summary>
    public Instant? BodySince(string key, string etag) =>
        _cache.TryGetValue(BodyKey(key), out BodyEntry? entry) && entry!.ETag == etag ? entry.Since : null;

    public void Set(string key, string state, RenderedFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        _cache.Set(RenderKey(key, state), feed, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _duration,
            Size = Math.Max(1, feed.Body.Length),
        });
        _cache.Set(BodyKey(key), new BodyEntry(feed.ETag, feed.LastModified), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = BodyHistory,
            Size = BodyEntrySize,
            Priority = CacheItemPriority.High,
        });
    }

    public void Dispose() => _cache.Dispose();

    private static string RenderKey(string key, string state) => key + "|" + state;

    private static string BodyKey(string key) => key + "|body";

    private sealed record BodyEntry(string ETag, Instant Since);
}
