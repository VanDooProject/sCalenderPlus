using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Infrastructure.Email;

/// <summary>SMTP delivery settings (<c>Smtp__*</c>), required by the worker. Development: Mailpit on localhost:1025.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required(ErrorMessage = "Smtp:Host is required.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>
    /// <c>Auto</c> (default: implicit TLS on port 465, STARTTLS when offered otherwise), <c>StartTls</c>
    /// (required), <c>SslOnConnect</c>, or <c>None</c> (plain; local Mailpit only).
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    /// <summary>Optional; authenticates when set.</summary>
    public string? User { get; set; }

    public string? Password { get; set; }

    [Required(ErrorMessage = "Smtp:From is required.")]
    [EmailAddress(ErrorMessage = "Smtp:From must be an email address.")]
    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "sCalenderPlus";

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public enum SmtpSecurity
{
    Auto,
    None,
    StartTls,
    SslOnConnect,
}
