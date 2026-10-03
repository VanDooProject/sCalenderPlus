using SCalenderPlus.Application.Email;

namespace SCalenderPlus.Application.Tests;

public sealed class EmailTemplateTests
{
    private static readonly EmailTemplate _template = new(
        "Join <Lions>",
        ["Adam invited you to \"Lions\" & friends."],
        new EmailAction("Accept invite", new Uri("https://app.example.test/invites/abc?x=1&y=<2>")));

    [Fact]
    public void Text_part_contains_paragraphs_and_the_action_link_verbatim()
    {
        var message = _template.Render("eve@example.test", "Eve");

        Assert.Equal("eve@example.test", message.To);
        Assert.Equal("Eve", message.ToName);
        Assert.Equal("Join <Lions>", message.Subject);
        Assert.Contains("Adam invited you to \"Lions\" & friends.\n\n", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Accept invite: https://app.example.test/invites/abc?x=1&y=%3C2%3E", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_part_encodes_every_value()
    {
        var html = _template.Render("eve@example.test").HtmlBody;

        Assert.Contains("<title>Join &lt;Lions&gt;</title>", html, StringComparison.Ordinal);
        Assert.Contains("Adam invited you to &quot;Lions&quot; &amp; friends.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.example.test/invites/abc?x=1&amp;y=%3C2%3E\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Lions>", html, StringComparison.Ordinal);
    }
}
