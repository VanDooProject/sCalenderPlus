using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Configuration;

namespace SCalenderPlus.Application.Tests;

public sealed class AccountEmailsTests
{
    private static readonly Guid _userId = Guid.Parse("0192f2c4-0000-7000-8000-000000000001");
    private readonly AccountEmails _emails = new(Options.Create(new AppOptions { PublicBaseUrl = "https://app.example.test/" }), Options.Create(new BrandOptions()));

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

    [Theory]
    [InlineData("en", "Europe/Berlin", "On October 31, 2026 we delete it")]
    [InlineData("de", "Europe/Berlin", "Am 31. Oktober 2026 löschen wir es")]
    [InlineData("en", "Pacific/Honolulu", "On October 30, 2026 we delete it")] // the date in the user's zone
    public void Deletion_confirmation_names_the_purge_date_and_links_to_the_account_settings(string locale, string zone, string sentence)
    {
        var message = _emails.DeletionScheduled("mia@example.test", "Mia", locale, zone, NodaTime.Instant.FromUtc(2026, 10, 31, 8, 0));

        Assert.Contains(sentence, message.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://app.example.test/settings/security", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleted_account_email_has_no_link() =>
        Assert.DoesNotContain("https://", _emails.AccountDeleted("mia@example.test", "Mia", "de").TextBody, StringComparison.Ordinal);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Every_account_email_names_the_configured_product(string locale)
    {
        var emails = new AccountEmails(
            Options.Create(new AppOptions { PublicBaseUrl = "https://app.example.test/" }),
            Options.Create(new BrandOptions { ProductName = "Lions Calendar" }));

        foreach (var message in new[]
        {
            emails.EmailConfirmation("mia@example.test", "Mia", locale, _userId, "tok"),
            emails.PasswordReset("mia@example.test", "Mia", locale, _userId, "tok"),
            emails.AlreadyRegistered("mia@example.test", "Mia", locale),
            emails.DeletionScheduled("mia@example.test", "Mia", locale, "UTC", NodaTime.Instant.FromUtc(2026, 10, 31, 8, 0)),
            emails.AccountDeleted("mia@example.test", "Mia", locale),
        })
        {
            Assert.Contains("Lions Calendar", message.TextBody.Split("-- \n")[0], StringComparison.Ordinal); // body, not only the footer
            Assert.DoesNotContain("sCalenderPlus", message.Subject + message.TextBody + message.HtmlBody, StringComparison.Ordinal);
        }
    }
}
