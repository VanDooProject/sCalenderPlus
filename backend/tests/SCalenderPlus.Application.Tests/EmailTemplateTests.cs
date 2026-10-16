using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;

namespace SCalenderPlus.Application.Tests;

public sealed class EmailTemplateTests
{
    private static readonly EmailTemplate _template = new(
        new BrandOptions(),
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

    [Fact]
    public void Footer_carries_the_default_product_name()
    {
        var message = _template.Render("eve@example.test");

        Assert.EndsWith("-- \nsCalenderPlus\n", message.TextBody, StringComparison.Ordinal);
        Assert.Contains(">sCalenderPlus</p>", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_carries_the_configured_brand_and_support_address_encoded()
    {
        var brand = new BrandOptions { ProductName = "Lions & <Co>", SupportEmail = "help@lions.example" };

        var message = (_template with { Brand = brand }).Render("eve@example.test");

        Assert.EndsWith("-- \nLions & <Co>\nhelp@lions.example\n", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Lions &amp; &lt;Co&gt; · <a href=\"mailto:help@lions.example\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sCalenderPlus", message.TextBody + message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_support_link_percent_encodes_the_address_so_it_cannot_add_mailto_fields()
    {
        // [EmailAddress] accepts this; unencoded, "?" and "&" would start mailto: header fields (subject, body).
        var brand = new BrandOptions { SupportEmail = "help+x?subject=Hi&body=\"<b>\"@lions.example" };

        var html = (_template with { Brand = brand }).Render("eve@example.test").HtmlBody;

        Assert.Contains("<a href=\"mailto:help%2Bx%3Fsubject%3DHi%26body%3D%22%3Cb%3E%22@lions.example\"", html, StringComparison.Ordinal);
        Assert.Contains(">help+x?subject=Hi&amp;body=&quot;&lt;b&gt;&quot;@lions.example</a>", html, StringComparison.Ordinal); // the text stays readable, HTML-encoded
        Assert.DoesNotContain("mailto:help+", html, StringComparison.Ordinal);
    }
}
