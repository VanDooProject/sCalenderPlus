namespace SCalenderPlus.Application.Email;

/// <summary>A rendered email (plain text and HTML alternative) to one recipient.</summary>
public sealed record EmailMessage(string To, string? ToName, string Subject, string TextBody, string HtmlBody);
