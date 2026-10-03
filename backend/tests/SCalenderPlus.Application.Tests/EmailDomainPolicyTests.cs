using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Accounts;

namespace SCalenderPlus.Application.Tests;

public sealed class EmailDomainPolicyTests
{
    [Theory]
    [InlineData("a@mailinator.com", true)]
    [InlineData("a@sub.mailinator.com", true)]
    [InlineData("a@MAILINATOR.com.", true)]
    [InlineData("a@notmailinator.com", false)]
    [InlineData("a@example.test", false)]
    [InlineData("a@blocked.example", true)]
    [InlineData("no-at-sign", false)]
    public void Disposable_and_configured_domains_are_blocked_with_subdomains(string email, bool blocked)
    {
        var policy = new EmailDomainPolicy(Options.Create(new SignUpOptions { BlockedEmailDomains = " blocked.example ,other.example" }));

        Assert.Equal(blocked, policy.IsBlocked(email));
    }

    [Fact]
    public void Bundled_list_can_be_switched_off()
    {
        var policy = new EmailDomainPolicy(Options.Create(new SignUpOptions { BlockDisposableEmailDomains = false }));

        Assert.False(policy.IsBlocked("a@mailinator.com"));
    }

    [Fact]
    public void Bundled_list_is_loaded_and_normalized()
    {
        Assert.Contains("yopmail.com", EmailDomainPolicy.BundledDomains);
        Assert.All(EmailDomainPolicy.BundledDomains, d => Assert.Equal(d.Trim().ToLowerInvariant(), d));
    }
}
