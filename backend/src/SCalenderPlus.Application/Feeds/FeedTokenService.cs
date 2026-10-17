using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Feeds;

/// <summary>A new personal feed token.</summary>
/// <param name="CalendarId">Required for <see cref="FeedScope.Calendar"/>, null otherwise.</param>
public sealed record NewFeedToken(
    FeedScope Scope,
    Guid? CalendarId = null,
    string? Name = null,
    FeedLabelMode LabelMode = FeedLabelMode.PrefixEmoji,
    int IncludePastDays = FeedToken.DefaultIncludePastDays);

/// <summary>Settings to change; null members stay unchanged (an empty <see cref="Name"/> removes it).</summary>
public sealed record FeedTokenChanges(string? Name = null, FeedLabelMode? LabelMode = null, int? IncludePastDays = null);

/// <summary>A token with its secret and feed URLs — returned only when the secret is created (create, rotate), never again.</summary>
public sealed record IssuedFeedToken(FeedToken Token, string Secret, Uri Url, Uri WebcalUrl);

/// <summary>
/// Personal iCal feed tokens (ical-caldav.md §1.1, api.md "Feeds"): every user manages only their own. The secret (32
/// random bytes, base64url) is shown once — on creation and rotation — and stored as SHA-256 only; rotation replaces
/// it (the old URL answers 404 at once); revocation keeps the row (audit, "last used") and answers 404 too. A
/// password reset may rotate all of a user's tokens without showing the new secrets
/// (<see cref="RotateAllForPasswordResetAsync"/>: <see cref="FeedToken.NeedsNewLink"/>).
/// </summary>
public sealed class FeedTokenService(
    IAppDbContext db,
    CalendarAccessLoader access,
    IAuditLog audit,
    IOptions<FeedOptions> options,
    IOptions<AppOptions> app,
    IClock clock)
{
    /// <summary>The caller's active tokens, by creation.</summary>
    public async Task<IReadOnlyList<FeedToken>> ListAsync(Guid actorId, CancellationToken cancellationToken = default) =>
        await db.FeedTokens.AsNoTracking().Where(t => t.UserId == actorId && t.RevokedAt == null).OrderBy(t => t.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <exception cref="AppException"><c>not_found</c> for revoked tokens and tokens of others.</exception>
    public async Task<FeedToken> GetAsync(Guid actorId, Guid tokenId, CancellationToken cancellationToken = default) =>
        await db.FeedTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tokenId && t.UserId == actorId && t.RevokedAt == null, cancellationToken).ConfigureAwait(false)
            ?? throw NotFound();

    /// <summary>
    /// Creates a token. A calendar token needs a calendar the caller sees (level ≥ <c>free_busy</c>, else 404);
    /// aggregate and "Shared with me" tokens take no calendar (400). At most <see cref="FeedOptions.MaxTokensPerUser"/>
    /// active tokens (409).
    /// </summary>
    public async Task<IssuedFeedToken> CreateAsync(Guid actorId, NewFeedToken request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Scope))
        {
            throw Validation.Failed("scope", "Use calendar, aggregate or shared_with_me.");
        }

        if (request.Scope == FeedScope.Calendar)
        {
            if (request.CalendarId is not { } calendarId)
            {
                throw Validation.Failed("calendarId", "A calendar feed needs the calendar.");
            }

            await access.RequireAsync(actorId, calendarId, CalendarAction.View, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else if (request.CalendarId is not null)
        {
            throw Validation.Failed("calendarId", "Only calendar feeds name a calendar.");
        }

        var name = ValidName(request.Name);
        ValidateSettings(request.LabelMode, request.IncludePastDays);

        return await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(actorId, ct).ConfigureAwait(false); // the count and the insert of concurrent creations
            var active = await db.FeedTokens.CountAsync(t => t.UserId == actorId && t.RevokedAt == null, ct).ConfigureAwait(false);
            if (active >= options.Value.MaxTokensPerUser)
            {
                throw new AppException(ErrorCodes.Conflict, $"You have {active} feed links; revoke one before creating another (at most {options.Value.MaxTokensPerUser}).");
            }

            var now = clock.Now();
            var secret = SecretTokens.Generate();
            var token = new FeedToken
            {
                Id = Guid.CreateVersion7(),
                UserId = actorId,
                Scope = request.Scope,
                CalendarId = request.CalendarId,
                Name = name,
                TokenHash = SecretTokens.Hash(secret),
                LabelMode = request.LabelMode,
                IncludePastDays = request.IncludePastDays,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.FeedTokens.Add(token);
            audit.Record(FeedAuditActions.Created, FeedAuditActions.ResourceType, token.Id.ToString(), null, State(token), actorId);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Issue(token, secret);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="precondition">Checks If-Match against the token's current state (after authorization); throws to refuse.</param>
    public async Task<FeedToken> UpdateAsync(Guid actorId, Guid tokenId, FeedTokenChanges changes, Action<FeedToken>? precondition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var token = await LoadAsync(actorId, tokenId, cancellationToken).ConfigureAwait(false);
        precondition?.Invoke(token);
        var name = changes.Name is null ? token.Name : ValidName(changes.Name);
        var labelMode = changes.LabelMode ?? token.LabelMode;
        var pastDays = changes.IncludePastDays ?? token.IncludePastDays;
        ValidateSettings(labelMode, pastDays);
        if ((name, labelMode, pastDays) == (token.Name, token.LabelMode, token.IncludePastDays))
        {
            return token;
        }

        var before = State(token);
        token.Name = name;
        token.LabelMode = labelMode;
        token.IncludePastDays = pastDays;
        token.UpdatedAt = clock.Now();
        audit.Record(FeedAuditActions.Updated, FeedAuditActions.ResourceType, token.Id.ToString(), before, State(token), actorId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>Revokes the token: its URL answers 404 from now on (also when a render of it is cached).</summary>
    public async Task RevokeAsync(Guid actorId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await LoadAsync(actorId, tokenId, cancellationToken).ConfigureAwait(false);
        token.RevokedAt = token.UpdatedAt = clock.Now();
        audit.Record(FeedAuditActions.Revoked, FeedAuditActions.ResourceType, token.Id.ToString(), State(token), null, actorId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A new secret for the token (settings kept); the old URL stops working at once.</summary>
    public async Task<IssuedFeedToken> RotateAsync(Guid actorId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await LoadAsync(actorId, tokenId, cancellationToken).ConfigureAwait(false);
        var secret = Rotate(token, shown: true);
        audit.Record(FeedAuditActions.Rotated, FeedAuditActions.ResourceType, token.Id.ToString(), null, new { Reason = "user" }, actorId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Issue(token, secret);
    }

    /// <summary>New secrets for all of the caller's active tokens ("rotate all", e.g. after a lost phone), each shown once.</summary>
    public async Task<IReadOnlyList<IssuedFeedToken>> RotateAllAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var tokens = await db.FeedTokens.Where(t => t.UserId == actorId && t.RevokedAt == null).OrderBy(t => t.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var issued = tokens.Select(t => Issue(t, Rotate(t, shown: true))).ToList();
        foreach (var token in tokens)
        {
            audit.Record(FeedAuditActions.Rotated, FeedAuditActions.ResourceType, token.Id.ToString(), null, new { Reason = "rotate_all" }, actorId);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return issued;
    }

    /// <summary>
    /// Password reset with <c>rotateFeedTokens</c>: every active token of <paramref name="userId"/> gets a new secret
    /// nobody sees (the reset request is anonymous), so the old URLs answer 404 at once; the tokens keep their
    /// settings and are marked <see cref="FeedToken.NeedsNewLink"/> until the owner rotates them to get new links.
    /// Stages the changes; the caller saves them in its transaction.
    /// </summary>
    /// <returns>The number of rotated tokens.</returns>
    public async Task<int> RotateAllForPasswordResetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await db.FeedTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var token in tokens)
        {
            Rotate(token, shown: false);
            audit.Record(FeedAuditActions.Rotated, FeedAuditActions.ResourceType, token.Id.ToString(), null, new { Reason = "password_reset" }, userId);
        }

        return tokens.Count;
    }

    /// <summary>The feed URL of a secret: <c>{App__PublicBaseUrl}/ical/v1/{secret}.ics</c>.</summary>
    public Uri FeedUrl(string secret) => FeedUrls.Personal(app.Value.PublicBaseUrl, secret);

    private string Rotate(FeedToken token, bool shown)
    {
        var secret = SecretTokens.Generate();
        token.TokenHash = SecretTokens.Hash(secret);
        token.NeedsNewLink = !shown;
        token.RotatedAt = token.UpdatedAt = clock.Now();
        return secret;
    }

    private IssuedFeedToken Issue(FeedToken token, string secret)
    {
        var url = FeedUrl(secret);
        return new IssuedFeedToken(token, secret, url, FeedUrls.Webcal(url));
    }

    private async Task<FeedToken> LoadAsync(Guid actorId, Guid tokenId, CancellationToken cancellationToken) =>
        await db.FeedTokens.SingleOrDefaultAsync(t => t.Id == tokenId && t.UserId == actorId && t.RevokedAt == null, cancellationToken).ConfigureAwait(false)
            ?? throw NotFound();

    private static string? ValidName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > FeedToken.NameMaxLength
            ? throw Validation.Failed("name", $"Use at most {FeedToken.NameMaxLength} characters.")
            : trimmed;
    }

    private static void ValidateSettings(FeedLabelMode labelMode, int includePastDays)
    {
        if (!Enum.IsDefined(labelMode))
        {
            throw Validation.Failed("labelMode", "Use prefix_emoji, prefix_emoji_both, suffix_text, description_only or none.");
        }

        if (includePastDays is < 0 or > FeedToken.MaxIncludePastDays)
        {
            throw Validation.Failed("includePastDays", $"Use 0 to {FeedToken.MaxIncludePastDays} days.");
        }
    }

    private static AppException NotFound() => new(ErrorCodes.NotFound, "Feed link not found.");

    private static object State(FeedToken token) => new
    {
        TokenId = token.Id,
        Scope = token.Scope.ToString(),
        token.CalendarId,
        token.Name,
        LabelMode = token.LabelMode.ToString(),
        token.IncludePastDays,
    };
}

/// <summary>Feed URLs (ical-caldav.md §1.1): <c>https</c> for copying, <c>webcal</c> for one-tap subscription.</summary>
public static class FeedUrls
{
    public static Uri Personal(string baseUrl, string secret) => Build(baseUrl, $"ical/v1/{secret}.ics");

    public static Uri ShareLink(string baseUrl, string secret) => Build(baseUrl, $"ical/v1/s/{secret}.ics");

    /// <summary>The same URL with the <c>webcal</c> scheme (calendar apps subscribe when it is opened).</summary>
    public static Uri Webcal(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new UriBuilder(url) { Scheme = "webcal", Port = url.IsDefaultPort ? -1 : url.Port }.Uri;
    }

    private static Uri Build(string baseUrl, string path)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        return new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute), path);
    }
}

/// <summary>Audit actions of feed tokens (resource type <see cref="ResourceType"/>, resource id = token id, subject = owner). Secrets and hashes never appear.</summary>
public static class FeedAuditActions
{
    public const string ResourceType = "feed_token";

    public const string Created = "feed_token.created";
    public const string Updated = "feed_token.updated";
    public const string Revoked = "feed_token.revoked";

    /// <summary><c>after.reason</c>: <c>user</c>, <c>rotate_all</c> or <c>password_reset</c>.</summary>
    public const string Rotated = "feed_token.rotated";
}
