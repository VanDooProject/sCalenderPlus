using System.Globalization;
using System.Net;
using System.Text;

namespace SCalenderPlus.Application.Email;

/// <summary>
/// Minimal transactional email layout: subject, paragraphs and an optional call-to-action link, rendered as
/// plain text and as simple, inline-styled HTML (all values HTML-encoded). Templates for concrete emails
/// (verification, password reset, invites) build one of these from localized texts.
/// </summary>
public sealed record EmailTemplate(string Subject, IReadOnlyList<string> Paragraphs, EmailAction? Action = null)
{
    public const string ProductName = "sCalenderPlus";

    public EmailMessage Render(string to, string? toName = null) =>
        new(to, toName, Subject, RenderText(), RenderHtml());

    private string RenderText()
    {
        var text = new StringBuilder();
        foreach (var paragraph in Paragraphs)
        {
            text.Append(paragraph).Append("\n\n");
        }

        if (Action is { } action)
        {
            text.Append(CultureInfo.InvariantCulture, $"{action.Label}: {action.Url.AbsoluteUri}\n\n");
        }

        return text.Append("-- \n").Append(ProductName).Append('\n').ToString();
    }

    private string RenderHtml()
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder()
            .Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(Subject)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#f6f7f9;font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#1f2933\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:24px\">");
        foreach (var paragraph in Paragraphs)
        {
            html.Append("<p style=\"margin:0 0 16px;line-height:1.5\">").Append(E(paragraph)).Append("</p>");
        }

        if (Action is { } action)
        {
            html.Append("<p style=\"margin:24px 0\"><a href=\"").Append(E(action.Url.AbsoluteUri))
                .Append("\" style=\"background:#2563eb;color:#ffffff;padding:10px 16px;border-radius:6px;text-decoration:none\">")
                .Append(E(action.Label)).Append("</a></p>");
        }

        return html.Append("<p style=\"margin:24px 0 0;color:#6b7280;font-size:12px\">").Append(ProductName).Append("</p>")
            .Append("</div></body></html>").ToString();
    }
}

/// <summary>Call-to-action link. Must be absolute (built from <c>App:PublicBaseUrl</c>).</summary>
public sealed record EmailAction(string Label, Uri Url);
