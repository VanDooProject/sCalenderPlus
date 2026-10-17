using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Core.Users;
using Calendar = SCalenderPlus.Core.Calendars.Calendar;

namespace SCalenderPlus.Application.Feeds;

/// <summary>How a feed request ends.</summary>
public enum FeedStatus
{
    Ok,

    /// <summary><c>If-None-Match</c> (or <c>If-Modified-Since</c>) matched the body: 304 (without loading events while the render is cached).</summary>
    NotModified,

    /// <summary>Unknown, malformed, revoked or expired token: 404 (no oracle).</summary>
    NotFound,

    /// <summary>The token's owner is being deleted: 410, so clients stop polling sooner.</summary>
    Gone,
}

/// <summary>The outcome of a feed request; <see cref="Feed"/> is set for <see cref="FeedStatus.Ok"/>.</summary>
/// <param name="ETag">The entity tag of the body (<see cref="RenderedFeed.ETag"/>).</param>
/// <param name="LastModified">Since when this replica has served this body (<see cref="RenderedFeed.LastModified"/>).</param>
public sealed record FeedResponse(FeedStatus Status, RenderedFeed? Feed = null, string? ETag = null, Instant? LastModified = null)
{
    public static FeedResponse NotFound { get; } = new(FeedStatus.NotFound);

    public static FeedResponse Gone { get; } = new(FeedStatus.Gone);
}

/// <summary>The conditional request headers of a feed request.</summary>
/// <param name="IfNoneMatch">The entity tags of <c>If-None-Match</c> (as sent, weak or strong; <c>*</c> matches any).</param>
public sealed record FeedConditions(IReadOnlyList<string> IfNoneMatch, Instant? IfModifiedSince)
{
    public static FeedConditions None { get; } = new([], null);
}

/// <summary>
/// Serves iCal feeds (ical-caldav.md §1): personal feeds <c>/ical/v1/{token}.ics</c> evaluated as the token's owner
/// and anonymous share-link feeds <c>/ical/v1/s/{token}.ics</c> evaluated as the <c>anonymous</c> principal with
/// the link's level as ceiling — both through the permission engine, per event, on every request. Order of work
/// (§1.4): (1) the token is resolved and validated (revoked/expired/unknown → 404, owner being deleted → 410) —
/// always first, so a revoked token never reaches a cached render; (2) the ETag is computed from a few cheap
/// queries that load no event rows (sync-log sequence numbers, <c>acl_version</c>s, settings) — the <em>state</em>, an
/// internal cache key only; (3) a render cached under token + state whose body ETag matches <c>If-None-Match</c>
/// answers 304 without loading events; (4) otherwise the cached render, or a fresh render — which answers 304 too when
/// its body is the one the client has. The ETag is the hash of the body, so a change the viewer cannot see (a hidden
/// field of a busy event, the calendar of an ex-member) changes neither the ETag nor <c>Last-Modified</c>.
/// </summary>
public sealed class FeedService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService events,
    IUserDirectory users,
    IEntitlementService entitlements,
    FeedCache cache,
    IOptions<FeedOptions> options,
    IOptions<AppOptions> app,
    IOptions<BrandOptions> brand,
    IClock clock)
{
    /// <summary><c>last_used_at</c> is written at most this often per token or link (not on every poll).</summary>
    public static readonly Duration LastUsedResolution = Duration.FromMinutes(15);

    /// <summary>Past days of share-link feeds (they have no settings).</summary>
    public const int ShareLinkPastDays = FeedToken.DefaultIncludePastDays;

    /// <summary>A personal feed.</summary>
    public async Task<FeedResponse> UserFeedAsync(string token, FeedConditions conditions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (!SecretTokens.IsWellFormed(token))
        {
            return FeedResponse.NotFound;
        }

        var hash = SecretTokens.Hash(token);
        var row = await db.FeedTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null)
        {
            return FeedResponse.NotFound;
        }

        var owner = await users.FeedOwnerAsync(row.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return FeedResponse.NotFound;
        }

        if (owner.Deleted)
        {
            return FeedResponse.Gone;
        }

        var now = clock.Now();
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(owner.TimeZone) ?? DateTimeZone.Utc;
        var fromDate = now.InZone(zone).Date.PlusDays(-row.IncludePastDays);
        var from = zone.AtStartOfDay(fromDate).ToInstant();
        var principal = await calendars.PrincipalAsync(owner.Id, cancellationToken).ConfigureAwait(false);
        var texts = FeedTexts.For(owner.Locale);

        // What the feed covers: the token's calendar; or every visible, not hidden calendar plus "Shared with me";
        // or only "Shared with me".
        List<(Calendar Calendar, CalendarAcl Acl)> included = [];
        IReadOnlyCollection<Guid> visibleIds = [];
        string name;
        switch (row.Scope)
        {
            case FeedScope.Calendar:
                if (await calendars.FindAclAsync(row.CalendarId!.Value, cancellationToken).ConfigureAwait(false) is not { } calendar)
                {
                    return FeedResponse.NotFound;
                }

                included.Add(calendar);

                // A calendar the owner no longer sees keeps feeding events shared with them, but not its (new) name.
                name = PermissionEngine.ResolveCalendarLevel(principal, calendar.Acl) == CalendarLevel.None ? brand.Value.ProductName : calendar.Calendar.Name;
                break;
            case FeedScope.Aggregate:
                var visible = await calendars.VisibleAsync(principal, cancellationToken: cancellationToken).ConfigureAwait(false);
                var hidden = (await db.CalendarPrefs.AsNoTracking().Where(p => p.UserId == owner.Id && p.Hidden).Select(p => p.CalendarId)
                    .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
                visibleIds = [.. visible.Select(v => v.Calendar.Id)];
                included.AddRange(visible.Where(v => !hidden.Contains(v.Calendar.Id)).Select(v => (v.Calendar, v.Acl)));
                name = brand.Value.ProductName;
                break;
            default:
                visibleIds = [.. (await calendars.VisibleAsync(principal, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(v => v.Calendar.Id)];
                name = texts.SharedWithMe;
                break;
        }

        var sharedWithMe = row.Scope != FeedScope.Calendar;
        var state = new FeedState()
            .Add("user")
            .Add(row.Id).Add((int)row.Scope).Add((int)row.LabelMode).Add(row.IncludePastDays)
            .Add(owner.Locale).Add(zone.Id).Add(owner.AclVersion).Add(fromDate.ToString("R", CultureInfo.InvariantCulture))
            .Add(name);
        var groupIds = principal.Groups.Keys.ToList();
        var groupVersions = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.AclVersion }).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var group in groupVersions)
        {
            state.Add(group.Id).Add(group.AclVersion);
        }

        await AddCalendarStateAsync(state, included, cancellationToken).ConfigureAwait(false);
        if (sharedWithMe)
        {
            // Changes of the events shared with the user (any calendar); a share added, changed or removed also
            // bumps the user's or the group's acl_version above.
            var userId = owner.Id;
            var sharedSeq = await db.CalendarChanges.AsNoTracking()
                .Where(c => db.EventOverrides.Any(o => o.EventId == c.EventId
                    && ((o.PrincipalType == PrincipalType.User && o.PrincipalId == userId)
                        || (o.PrincipalType == PrincipalType.Group && groupIds.Contains(o.PrincipalId!.Value)))))
                .MaxAsync(c => (long?)c.Seq, cancellationToken).ConfigureAwait(false);
            state.Add("shared").Add(sharedSeq ?? 0);
            foreach (var id in visibleIds.Order())
            {
                state.Add(id);
            }

            // The calendars of the shared events the user does not see (their ACL and settings take part in each
            // event's level): found through the sync log, which names every calendar an event has been in.
            var known = visibleIds.Concat(included.Select(c => c.Calendar.Id)).Distinct().ToList();
            var extraIds = await db.CalendarChanges.AsNoTracking()
                .Where(c => !known.Contains(c.CalendarId) && db.EventOverrides.Any(o => o.EventId == c.EventId
                    && ((o.PrincipalType == PrincipalType.User && o.PrincipalId == userId)
                        || (o.PrincipalType == PrincipalType.Group && groupIds.Contains(o.PrincipalId!.Value)))))
                .Select(c => c.CalendarId).Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var extras = extraIds.Count == 0
                ? []
                : await db.Calendars.AsNoTracking().Where(c => extraIds.Contains(c.Id)).OrderBy(c => c.Id)
                    .Select(c => new { c.Id, c.AclVersion, c.Version })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            state.Add("extra");
            foreach (var extra in extras)
            {
                state.Add(extra.Id).Add(extra.AclVersion).Add(extra.Version);
            }
        }

        var plan = (await entitlements.GetAsync(owner.Id, cancellationToken).ConfigureAwait(false)).Plan;
        state.Add(plan.ToString());
        var stateKey = state.ToKey(FeedRenderer.FormatVersion, brand.Value.ProductName, app.Value.PublicBaseUrl);

        var key = "u:" + Convert.ToHexString(hash);
        var response = await RespondAsync(key, stateKey, conditions, async () =>
        {
            var views = await events.FeedAsync(principal, included, sharedWithMe, visibleIds, from, options.Value.MaxEvents, cancellationToken).ConfigureAwait(false);
            return new FeedDocument(
                name,
                zone.Id,
                texts,
                row.LabelMode,
                NativeUids: row.Scope == FeedScope.Calendar,
                AccessProperties: true,
                brand.Value.ProductName,
                AppBase(),
                now,
                Cap(views));
        }).ConfigureAwait(false);

        if (row.LastUsedAt is null || now - row.LastUsedAt.Value >= LastUsedResolution)
        {
            await db.FeedTokens.Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>An anonymous share-link feed: the calendar as the <c>anonymous</c> principal sees it, the link's level as ceiling.</summary>
    public async Task<FeedResponse> ShareLinkFeedAsync(string token, FeedConditions conditions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (!SecretTokens.IsWellFormed(token))
        {
            return FeedResponse.NotFound;
        }

        var hash = SecretTokens.Hash(token);
        var now = clock.Now();
        var link = await db.ShareLinks.AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        if (link is null || !link.IsActive(now)
            || await calendars.FindAclAsync(link.CalendarId, cancellationToken).ConfigureAwait(false) is not { } calendar)
        {
            return FeedResponse.NotFound;
        }

        var principal = PrincipalContext.ForShareLink(link.CalendarId, link.Level);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(calendar.Calendar.DefaultTimeZone) ?? DateTimeZone.Utc;
        var fromDate = now.InZone(zone).Date.PlusDays(-ShareLinkPastDays);
        var from = zone.AtStartOfDay(fromDate).ToInstant();
        var creator = (await users.GetAsync([link.CreatedBy], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(link.CreatedBy);
        var locale = creator?.Locale ?? UserPreferences.DefaultLocale;

        var state = new FeedState()
            .Add("link").Add(link.Id).Add((int)link.Level).Add(locale).Add(zone.Id).Add(fromDate.ToString("R", CultureInfo.InvariantCulture));
        await AddCalendarStateAsync(state, [calendar], cancellationToken).ConfigureAwait(false);
        var stateKey = state.ToKey(FeedRenderer.FormatVersion, brand.Value.ProductName, app.Value.PublicBaseUrl);

        var key = "s:" + Convert.ToHexString(hash);
        var response = await RespondAsync(key, stateKey, conditions, async () =>
        {
            var views = await events.FeedAsync(principal, [calendar], false, [], from, options.Value.MaxEvents, cancellationToken).ConfigureAwait(false);
            return new FeedDocument(
                calendar.Calendar.Name,
                zone.Id,
                FeedTexts.For(locale),
                FeedLabelMode.None,
                NativeUids: true,
                AccessProperties: false,
                brand.Value.ProductName,
                AppBase(),
                now,
                Cap(views));
        }).ConfigureAwait(false);

        if (link.LastUsedAt is null || now - link.LastUsedAt.Value >= LastUsedResolution)
        {
            await db.ShareLinks.Where(l => l.Id == link.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastUsedAt, now), cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>Whether <paramref name="ifNoneMatch"/> matches <paramref name="etag"/> (weak comparison, RFC 9110 §13.1.2).</summary>
    public static bool Matches(IReadOnlyList<string> ifNoneMatch, string etag)
    {
        ArgumentNullException.ThrowIfNull(ifNoneMatch);
        var opaque = Opaque(etag);
        return ifNoneMatch.Any(candidate => candidate.Trim() == "*" || Opaque(candidate) == opaque);
    }

    private static string Opaque(string tag)
    {
        tag = tag.Trim();
        return tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
    }

    /// <summary>Per included calendar: id, <c>acl_version</c>, row version (name and settings) and its latest sync-log sequence number.</summary>
    private async Task AddCalendarStateAsync(FeedState state, IReadOnlyList<(Calendar Calendar, CalendarAcl Acl)> included, CancellationToken cancellationToken)
    {
        var ids = included.Select(c => c.Calendar.Id).ToList();
        var seqs = ids.Count == 0
            ? []
            : await db.CalendarChanges.AsNoTracking().Where(c => ids.Contains(c.CalendarId))
                .GroupBy(c => c.CalendarId).Select(g => new { CalendarId = g.Key, Seq = g.Max(c => c.Seq) })
                .ToDictionaryAsync(g => g.CalendarId, g => g.Seq, cancellationToken).ConfigureAwait(false);
        foreach (var (calendar, _) in included.OrderBy(c => c.Calendar.Id))
        {
            state.Add(calendar.Id).Add(calendar.AclVersion).Add(calendar.Version).Add(seqs.GetValueOrDefault(calendar.Id));
        }
    }

    /// <summary>The entity tag of a feed body: <c>W/"&lt;base64url of 16 bytes of its SHA-256&gt;"</c> (weak: the api compresses the body).</summary>
    public static string BodyETag(byte[] body) => "W/\"" + Base64Url(SHA256.HashData(body)) + "\"";

    /// <summary>
    /// §1.4: a render cached under the token and the state answers from memory (304 without loading events when
    /// its body ETag is the client's); otherwise the feed is rendered, and a body equal to the one the client has
    /// still answers 304. <c>Last-Modified</c> is when this replica first served the body (kept per token in
    /// the cache, so a re-render with an unchanged body keeps it); <c>If-Modified-Since</c> is honoured only
    /// without <c>If-None-Match</c> and never claims more than that.
    /// </summary>
    private async Task<FeedResponse> RespondAsync(string key, string stateKey, FeedConditions conditions, Func<Task<FeedDocument>> document)
    {
        var cached = cache.Get(key, stateKey);
        if (cached is null)
        {
            var body = Encoding.UTF8.GetBytes(FeedRenderer.Render(await document().ConfigureAwait(false)));
            var etag = BodyETag(body);
            var lastModified = cache.BodySince(key, etag) ?? Instant.FromUnixTimeSeconds(clock.GetCurrentInstant().ToUnixTimeSeconds());
            cached = new RenderedFeed(body, etag, lastModified);
            cache.Set(key, stateKey, cached);
        }

        return NotModified(conditions, cached)
            ? new FeedResponse(FeedStatus.NotModified, null, cached.ETag, cached.LastModified)
            : new FeedResponse(FeedStatus.Ok, cached, cached.ETag, cached.LastModified);
    }

    private static bool NotModified(FeedConditions conditions, RenderedFeed feed) =>
        conditions.IfNoneMatch.Count > 0
            ? Matches(conditions.IfNoneMatch, feed.ETag)
            : conditions.IfModifiedSince is { } since && since >= feed.LastModified;

    private static string Base64Url(byte[] hash) => Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>At most <see cref="FeedOptions.MaxEvents"/> VEVENTs: the events ending latest are kept (oldest past events dropped first).</summary>
    private List<FeedItem> Cap(IReadOnlyList<EventView> views)
    {
        var items = new List<FeedItem>(views.Count);
        var count = 0;
        foreach (var view in views.OrderByDescending(v => v.Event.OccursUntil ?? Instant.MaxValue).ThenBy(v => v.Event.Id))
        {
            var item = new FeedItem(view.Event, view.Level);
            count += FeedRenderer.CountVEvents(item);
            if (count > options.Value.MaxEvents)
            {
                break;
            }

            items.Add(item);
        }

        return items;
    }

    private Uri AppBase()
    {
        var url = app.Value.PublicBaseUrl;
        return new Uri(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);
    }

    /// <summary>The state of a feed (§1.4): every input of its render, hashed — the internal cache key, never sent to clients.</summary>
    private sealed class FeedState
    {
        private readonly StringBuilder _parts = new();

        public FeedState Add(string value)
        {
            _parts.Append(value.Length).Append(':').Append(value).Append('|');
            return this;
        }

        public FeedState Add(long value) => Add(value.ToString(CultureInfo.InvariantCulture));

        public FeedState Add(Guid value) => Add(value.ToString("N"));

        public string ToKey(int formatVersion, string productName, string baseUrl)
        {
            Add(formatVersion).Add(productName).Add(baseUrl);
            return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(_parts.ToString())));
        }
    }
}
