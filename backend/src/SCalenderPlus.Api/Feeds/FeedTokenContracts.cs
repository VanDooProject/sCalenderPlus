using System.ComponentModel.DataAnnotations;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Core.Feeds;

namespace SCalenderPlus.Api.Feeds;

/// <summary>A personal iCal feed link (never contains the secret: that is only in <see cref="IssuedFeedTokenResponse"/>).</summary>
/// <param name="Scope"><c>calendar</c>, <c>aggregate</c> (all my calendars plus "Shared with me") or <c>shared_with_me</c>.</param>
/// <param name="CalendarId">The calendar of a <c>calendar</c> feed; null otherwise.</param>
/// <param name="Name">My label for the subscription (e.g. "iPhone"); null = none.</param>
/// <param name="LabelMode">How read-only and editable events are marked: <c>prefix_emoji</c> (default), <c>prefix_emoji_both</c>, <c>suffix_text</c>, <c>description_only</c> or <c>none</c>.</param>
/// <param name="IncludePastDays">Past events from this many days ago are included (0–365, default 90).</param>
/// <param name="NeedsNewLink">The secret was replaced by a password reset: the old URL is dead; rotate to get a new link.</param>
/// <param name="LastUsedAt">Last feed request (updated at most every 15 minutes); null = never used.</param>
/// <param name="RotatedAt">Last secret rotation; null = never rotated.</param>
/// <param name="Etag">Send as <c>If-Match</c> to change these settings.</param>
public sealed record FeedTokenResponse(
    Guid Id,
    string Scope,
    Guid? CalendarId,
    string? Name,
    string LabelMode,
    int IncludePastDays,
    bool NeedsNewLink,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Etag)
{
    public static FeedTokenResponse From(FeedToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new FeedTokenResponse(
            token.Id,
            FeedTokenNames.Format(token.Scope),
            token.CalendarId,
            token.Name,
            FeedTokenNames.Format(token.LabelMode),
            token.IncludePastDays,
            token.NeedsNewLink,
            token.LastUsedAt?.ToDateTimeOffset(),
            token.RotatedAt?.ToDateTimeOffset(),
            token.CreatedAt.ToDateTimeOffset(),
            token.UpdatedAt.ToDateTimeOffset(),
            ETagOf(token));
    }

    /// <summary>Changes with the settings (not with <c>lastUsedAt</c>: polling must not break an edit).</summary>
    public static string ETagOf(FeedToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ETags.Of(new { token.Id, token.Name, token.LabelMode, token.IncludePastDays, UpdatedAt = token.UpdatedAt.ToDateTimeOffset() });
    }
}

/// <summary>A feed link with its secret, returned only when the secret is created (create, rotate) — store or show it now, it cannot be read again.</summary>
/// <param name="Token">The secret (43 characters, base64url); it is the credential of the feed.</param>
/// <param name="Url">The feed URL (<c>https</c>) to paste into a calendar app.</param>
/// <param name="WebcalUrl">The same URL with the <c>webcal</c> scheme: opening it subscribes on most devices (also for the QR code).</param>
public sealed record IssuedFeedTokenResponse(FeedTokenResponse FeedToken, string Token, Uri Url, Uri WebcalUrl)
{
    public static IssuedFeedTokenResponse From(IssuedFeedToken issued)
    {
        ArgumentNullException.ThrowIfNull(issued);
        return new IssuedFeedTokenResponse(FeedTokenResponse.From(issued.Token), issued.Secret, issued.Url, issued.WebcalUrl);
    }
}

public sealed record FeedTokenListResponse(IReadOnlyList<FeedTokenResponse> Items);

public sealed record IssuedFeedTokenListResponse(IReadOnlyList<IssuedFeedTokenResponse> Items);

public sealed class CreateFeedTokenRequest
{
    /// <summary><c>calendar</c>, <c>aggregate</c> or <c>shared_with_me</c>.</summary>
    [Required]
    public string Scope { get; init; } = string.Empty;

    /// <summary>Required for <c>calendar</c> feeds (a calendar I see); not allowed otherwise.</summary>
    public Guid? CalendarId { get; init; }

    /// <summary>My label for the subscription (≤ 100 characters).</summary>
    [StringLength(FeedToken.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>Default <c>prefix_emoji</c>.</summary>
    public string? LabelMode { get; init; }

    /// <summary>0–365, default 90.</summary>
    [Range(0, FeedToken.MaxIncludePastDays)]
    public int? IncludePastDays { get; init; }
}

/// <summary>JSON Merge Patch of a feed link's settings: absent or <c>null</c> members stay unchanged; an empty <c>name</c> removes it.</summary>
public sealed class UpdateFeedTokenRequest
{
    [StringLength(FeedToken.NameMaxLength)]
    public string? Name { get; init; }

    public string? LabelMode { get; init; }

    [Range(0, FeedToken.MaxIncludePastDays)]
    public int? IncludePastDays { get; init; }
}

/// <summary>API names of feed scopes and label modes.</summary>
internal static class FeedTokenNames
{
    public static string Format(FeedScope scope) => scope switch
    {
        FeedScope.Calendar => "calendar",
        FeedScope.Aggregate => "aggregate",
        _ => "shared_with_me",
    };

    public static string Format(FeedLabelMode mode) => mode switch
    {
        FeedLabelMode.PrefixEmoji => "prefix_emoji",
        FeedLabelMode.PrefixEmojiBoth => "prefix_emoji_both",
        FeedLabelMode.SuffixText => "suffix_text",
        FeedLabelMode.DescriptionOnly => "description_only",
        _ => "none",
    };

    public static FeedScope ParseScope(string? value) => value switch
    {
        "calendar" => FeedScope.Calendar,
        "aggregate" => FeedScope.Aggregate,
        "shared_with_me" => FeedScope.SharedWithMe,
        _ => throw Validation.Failed("scope", "Use calendar, aggregate or shared_with_me."),
    };

    public static FeedLabelMode ParseLabelMode(string? value) => value switch
    {
        "prefix_emoji" => FeedLabelMode.PrefixEmoji,
        "prefix_emoji_both" => FeedLabelMode.PrefixEmojiBoth,
        "suffix_text" => FeedLabelMode.SuffixText,
        "description_only" => FeedLabelMode.DescriptionOnly,
        "none" => FeedLabelMode.None,
        _ => throw Validation.Failed("labelMode", "Use prefix_emoji, prefix_emoji_both, suffix_text, description_only or none."),
    };
}
