namespace SCalenderPlus.Application.Accounts;

/// <summary>Sign-up abuse protection (environment variables <c>SignUp__*</c>).</summary>
public sealed class SignUpOptions
{
    public const string SectionName = "SignUp";

    /// <summary>Reject the bundled list of disposable-email providers (default <c>true</c>).</summary>
    public bool BlockDisposableEmailDomains { get; set; } = true;

    /// <summary>Additional comma-separated domains to reject (subdomains included), e.g. <c>example.org,spam.test</c>.</summary>
    public string? BlockedEmailDomains { get; set; }
}
