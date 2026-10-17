using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>A share link with its secret and feed URLs — returned only at creation, never again.</summary>
public sealed record IssuedShareLink(ShareLink Link, string Secret, Uri Url, Uri WebcalUrl);

/// <summary>
/// Anonymous share links of calendars (permissions.md §3, api.md "Share links"): calendar managers create links with
/// level <c>free_busy</c> or <c>read</c> (<see cref="AccessPolicy.CanCreateShareLink"/>), which act as a ceiling for
/// their holders in the feed <c>/ical/v1/s/{token}.ics</c>. The secret is shown once and stored as SHA-256 only;
/// revoking keeps the row and the feed answers 404 at once. Every change bumps the calendar's <c>acl_version</c>
/// and is audited on the calendar.
/// </summary>
public sealed class ShareLinkService(
    IAppDbContext db,
    CalendarAccessLoader access,
    AclVersions versions,
    IAuditLog audit,
    IOptions<FeedOptions> options,
    IOptions<AppOptions> app,
    IClock clock)
{
    /// <summary>The calendar's links that are not revoked (expired ones included, with <see cref="ShareLink.ExpiresAt"/>), by creation; <c>manage</c> required.</summary>
    public async Task<IReadOnlyList<ShareLink>> ListAsync(Guid actorId, Guid calendarId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actorId, calendarId, CalendarAction.ManageSharing, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await db.ShareLinks.AsNoTracking().Where(l => l.CalendarId == calendarId && l.RevokedAt == null).OrderBy(l => l.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a link (<c>manage</c>; level <c>free_busy</c> or <c>read</c>, else 400; frozen calendars 409; at most
    /// <see cref="FeedOptions.MaxShareLinksPerCalendar"/> active links, 409).
    /// </summary>
    /// <param name="expiresInDays">1 … <see cref="ShareLink.MaxExpiryDays"/>; null = never expires.</param>
    public async Task<IssuedShareLink> CreateAsync(
        Guid actorId,
        Guid calendarId,
        CalendarLevel level,
        string? label,
        int? expiresInDays,
        CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (trimmed is { Length: > ShareLink.LabelMaxLength })
        {
            throw Validation.Failed("label", $"Use at most {ShareLink.LabelMaxLength} characters.");
        }

        if (expiresInDays is < 1 or > ShareLink.MaxExpiryDays)
        {
            throw Validation.Failed("expiresInDays", $"Use 1 to {ShareLink.MaxExpiryDays} days, or leave it out for a link that does not expire.");
        }

        return await db.InTransactionAsync(async ct =>
        {
            var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.ManageSharing, cancellationToken: ct).ConfigureAwait(false);
            if (!AccessPolicy.CanCreateShareLink(loaded.Level, level))
            {
                throw Validation.Failed("level", "Share links give free_busy or read.");
            }

            if (loaded.Calendar.FrozenAt is not null)
            {
                throw CalendarErrors.Frozen();
            }

            await db.LockAsync(calendarId, ct).ConfigureAwait(false);
            var active = await db.ShareLinks.CountAsync(l => l.CalendarId == calendarId && l.RevokedAt == null, ct).ConfigureAwait(false);
            if (active >= options.Value.MaxShareLinksPerCalendar)
            {
                throw new AppException(ErrorCodes.Conflict, $"The calendar has {active} share links; revoke one before creating another (at most {options.Value.MaxShareLinksPerCalendar}).");
            }

            var now = clock.Now();
            var secret = SecretTokens.Generate();
            var link = new ShareLink
            {
                Id = Guid.CreateVersion7(),
                CalendarId = calendarId,
                TokenHash = SecretTokens.Hash(secret),
                Level = level,
                Label = trimmed,
                ExpiresAt = expiresInDays is { } days ? now + Duration.FromDays(days) : null,
                CreatedBy = actorId,
                CreatedAt = now,
            };
            db.ShareLinks.Add(link);
            await versions.BumpCalendarAsync(calendarId, ct).ConfigureAwait(false);
            audit.Record(ShareLinkAuditActions.Created, CalendarAuditActions.ResourceType, calendarId.ToString(), null, State(link), await CalendarAudit.BillingSubjectAsync(db, loaded.Calendar, ct).ConfigureAwait(false));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var url = FeedUrls.ShareLink(app.Value.PublicBaseUrl, secret);
            return new IssuedShareLink(link, secret, url, FeedUrls.Webcal(url));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Revokes a link (<c>manage</c> on its calendar; unknown links and calendars the caller does not see 404).
    /// Idempotent; allowed in frozen calendars (taking access away never waits for a plan upgrade).
    /// </summary>
    public async Task RevokeAsync(Guid actorId, Guid linkId, CancellationToken cancellationToken = default) =>
        await db.InTransactionAsync(async ct =>
        {
            var link = await db.ShareLinks.SingleOrDefaultAsync(l => l.Id == linkId, ct).ConfigureAwait(false)
                ?? throw new AppException(ErrorCodes.NotFound, "Share link not found.");
            var loaded = await access.RequireAsync(actorId, link.CalendarId, CalendarAction.ManageSharing, cancellationToken: ct).ConfigureAwait(false);
            if (link.RevokedAt is not null)
            {
                return 0;
            }

            link.RevokedAt = clock.Now();
            await versions.BumpCalendarAsync(link.CalendarId, ct).ConfigureAwait(false);
            audit.Record(ShareLinkAuditActions.Revoked, CalendarAuditActions.ResourceType, link.CalendarId.ToString(), State(link), null, await CalendarAudit.BillingSubjectAsync(db, loaded.Calendar, ct).ConfigureAwait(false));
            return await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

    private static object State(ShareLink link) => new
    {
        ShareLinkId = link.Id,
        Level = PermissionLevels.Format(link.Level),
        link.Label,
        link.ExpiresAt,
    };
}

/// <summary>Audit actions of share links, recorded on their calendar (resource type <c>calendar</c>). Secrets and hashes never appear.</summary>
public static class ShareLinkAuditActions
{
    public const string Created = "calendar.share_link.created";
    public const string Revoked = "calendar.share_link.revoked";
}
