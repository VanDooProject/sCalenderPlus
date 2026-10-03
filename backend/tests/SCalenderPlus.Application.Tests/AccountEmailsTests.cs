using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Configuration;

namespace SCalenderPlus.Application.Tests;

public sealed class AccountEmailsTests
{
    private static readonly Guid _userId = Guid.Parse("0192f2c4-0000-7000-8000-000000000001");
    private readonly AccountEmails _emails = new(Options.Create(new AppOptions { PublicBaseUrl = "https://app.example.test/" }));

    [Fact]
    public void Confirmation_link_points_to_the_web_app_verify_route_with_an_escaped_token()
    {
        var message = _emails.EmailConfirmation("mia@example.test", "Mia", "en", _userId, "a+b/c=");

        Assert.Equal("Confirm your email address", message.Subject);
        Assert.Contains($"https://app.example.test/verify-email?userId={_userId}&token=a%2Bb%2Fc%3D", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_link_points_to_the_reset_route_and_german_users_get_german_text()
    {
        var message = _emails.PasswordReset("mia@example.test", "Mia", "de", _userId, "tok");

        Assert.Equal("Passwort zurücksetzen", message.Subject);
        Assert.Contains("Hallo Mia,", message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://app.example.test/reset-password?userId={_userId}&token=tok", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Already_registered_hint_links_to_forgot_password_and_contains_no_token()
    {
        var message = _emails.AlreadyRegistered("mia@example.test", "Mia", "en");

        Assert.Contains("https://app.example.test/forgot-password", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", message.TextBody, StringComparison.Ordinal);
    }
}
