using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Tests;

public sealed class GroupEmailsTests
{
    private static readonly Instant _expiresAt = Instant.FromUtc(2026, 10, 17, 18, 30);
    private readonly GroupEmails _emails = new(Options.Create(new AppOptions { PublicBaseUrl = "https://app.example.test/" }));

    [Fact]
    public void Invite_link_points_to_the_web_app_invite_route_with_an_escaped_token() =>
        Assert.Equal("https://app.example.test/invite?token=a-b_c%2B", _emails.InviteLink("a-b_c+").AbsoluteUri);

    [Fact]
    public void Invitation_names_inviter_group_role_and_expiry()
    {
        var message = _emails.Invitation("vic@example.test", "en", "Olga", "FC Lions", GroupRole.Admin, "tok", _expiresAt);

        Assert.Equal("vic@example.test", message.To);
        Assert.Equal("Invitation to the group “FC Lions”", message.Subject);
        Assert.Contains("Olga invites you to join the group “FC Lions” on sCalenderPlus as an admin.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("2026-10-17 18:30 UTC", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Accept invitation: https://app.example.test/invite?token=tok", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void German_recipients_get_german_text_and_values_are_html_encoded()
    {
        var message = _emails.Invitation("vic@example.test", "de", "Olga <script>", "Löwen & Co", GroupRole.Member, "tok", _expiresAt);

        Assert.Equal("Einladung in die Gruppe „Löwen & Co“", message.Subject);
        Assert.Contains("Olga <script> lädt dich als Mitglied in die Gruppe „Löwen & Co“", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Olga &lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
    }
}
