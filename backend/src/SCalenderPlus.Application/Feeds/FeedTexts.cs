namespace SCalenderPlus.Application.Feeds;

/// <summary>
/// The localized texts of feeds (ical-caldav.md §1.3), in the token owner's locale (<c>en</c>, <c>de</c>; anything
/// else falls back to English). <c>{0}</c> is the product name (<c>Brand__ProductName</c>), <c>{1}</c> the app link.
/// </summary>
public sealed record FeedTexts(
    string Busy,
    string SharedWithMe,
    string ReadOnlySuffix,
    string EditableSuffix,
    string ReadOnlyFooter,
    string EditableFooter)
{
    public const string ReadOnlyMarker = "🔒";
    public const string EditableMarker = "✏️";

    public static FeedTexts English { get; } = new(
        "Busy",
        "Shared with me",
        "[read-only]",
        "[editable]",
        ReadOnlyMarker + " Read-only for you · View in {0}: {1}",
        EditableMarker + " You can edit this event: {1}");

    public static FeedTexts German { get; } = new(
        "Beschäftigt",
        "Mit mir geteilt",
        "[schreibgeschützt]",
        "[bearbeitbar]",
        ReadOnlyMarker + " Für dich schreibgeschützt · In {0} ansehen: {1}",
        EditableMarker + " Du kannst diesen Termin bearbeiten: {1}");

    public static FeedTexts For(string? locale) => locale == "de" ? German : English;
}
