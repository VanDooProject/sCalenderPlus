using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>Issue #28: registration, email confirmation, login/logout, <c>GET /me</c> and password reset.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class AccountFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Register_queues_a_confirmation_email_whose_link_verifies_the_address()
    {
        using var client = _host.CreateClient();
        var email = ApiTestHost.UniqueEmail();

        using var register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = ApiTestHost.Password, displayName = "Mia", locale = "de", timeZone = "Europe/Berlin" },
            Ct);
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        Assert.False(register.Headers.Contains("Set-Cookie"), "Sign-up must not sign in (no account enumeration via cookies).");

        var mail = await _host.SingleEmailToAsync(email);
        Assert.Equal("Bestätige deine E-Mail-Adresse", mail.Subject);
        var (path, userId, token) = ApiTestHost.LinkIn(mail);
        Assert.Equal("/verify-email", path);

        // Unverified accounts can sign in.
        using (var login = await ApiTestHost.LoginAsync(client, email))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = JsonNode.Parse(await login.Content.ReadAsStringAsync(Ct))!;
            Assert.False((bool)body["twoFactorRequired"]!);
            Assert.False((bool)body["user"]!["emailVerified"]!);
        }

        using (var confirm = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token }, Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        var me = JsonNode.Parse(await client.GetStringAsync(new Uri("/api/v1/me", UriKind.Relative), Ct))!;
        Assert.Equal(userId.ToString(), (string?)me["id"]);
        Assert.Equal(email, (string?)me["email"]);
        Assert.True((bool)me["emailVerified"]!);
        Assert.Equal("Mia", (string?)me["displayName"]);
        Assert.Equal("de", (string?)me["locale"]);
        Assert.Equal("Europe/Berlin", (string?)me["timeZone"]);
        Assert.Equal("monday", (string?)me["weekStart"]);
        Assert.False((bool)me["twoFactorEnabled"]!);

        var actions = (await _host.AuditEventsAsync(userId)).Select(e => e.Action).ToList();
        Assert.Equal(["user.registered", "user.login_succeeded", "user.email_confirmed"], actions);
    }

    [Fact]
    public async Task Register_with_a_registered_address_answers_the_same_and_sends_a_hint_instead()
    {
        var email = ApiTestHost.UniqueEmail();
        var existingId = await _host.CreateUserAsync(email);
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = email.ToUpperInvariant(), password = "another password 1", displayName = "Mallory" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var mail = await _host.SingleEmailToAsync(email);
        Assert.Equal("You already have an account", mail.Subject);
        Assert.Contains("https://app.example.test/forgot-password", mail.TextBody, StringComparison.Ordinal);
        Assert.Equal(existingId, (await _host.FindUserAsync(email)).Id);
        using var login = await ApiTestHost.LoginAsync(client, email, "another password 1");
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_short_passwords_unknown_zones_and_locales()
    {
        using var client = _host.CreateClient();

        using var shortPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = ApiTestHost.UniqueEmail(), password = "short", displayName = "Mia" }, Ct);
        var problem = await ProblemResponse.AssertProblemAsync(shortPassword, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["password"]);

        using var zone = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = ApiTestHost.UniqueEmail(), password = ApiTestHost.Password, displayName = "Mia", timeZone = "Mars/Olympus" }, Ct);
        problem = await ProblemResponse.AssertProblemAsync(zone, HttpStatusCode.UnprocessableEntity, ErrorCodes.TimeZoneInvalid);
        Assert.NotNull(problem["errors"]!["timeZone"]);

        using var locale = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = ApiTestHost.UniqueEmail(), password = ApiTestHost.Password, displayName = "Mia", locale = "fr" }, Ct);
        problem = await ProblemResponse.AssertProblemAsync(locale, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["locale"]);

        using var missing = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "not-an-email", password = ApiTestHost.Password }, Ct);
        problem = await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.True(problem["errors"]!["email"] is not null, problem.ToJsonString());
        Assert.True(problem["errors"]!["displayName"] is not null, problem.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_cookie_is_host_prefixed_http_only_secure_and_same_site_lax(bool rememberMe)
    {
        var email = ApiTestHost.UniqueEmail();
        await _host.CreateUserAsync(email);
        using var client = _host.CreateClient();

        using var response = await ApiTestHost.LoginAsync(client, email, rememberMe: rememberMe);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-scal=", StringComparison.Ordinal));
        var attributes = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToList();
        Assert.Contains("path=/", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("domain=", StringComparison.Ordinal));
        Assert.Equal(rememberMe, attributes.Any(a => a.StartsWith("expires=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Unauthenticated_requests_get_a_401_problem_not_a_redirect()
    {
        using var client = _host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Failed_logins_are_indistinguishable_and_audited_for_known_accounts()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email);
        using var client = _host.CreateClient();

        using var wrongPassword = await ApiTestHost.LoginAsync(client, email, "wrong password 123");
        using var unknownEmail = await ApiTestHost.LoginAsync(client, ApiTestHost.UniqueEmail(), "wrong password 123");

        var first = await ProblemResponse.AssertProblemAsync(wrongPassword, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        var second = await ProblemResponse.AssertProblemAsync(unknownEmail, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        Assert.Equal((string?)first["detail"], (string?)second["detail"]);
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie"));
        var audit = Assert.Single(await _host.AuditEventsAsync(userId));
        Assert.Equal("user.login_failed", audit.Action);
        Assert.Equal("anonymous", audit.ActorKind);
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_and_a_password_reset_lifts_the_lockout()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email);
        using var client = _host.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var failed = await ApiTestHost.LoginAsync(client, email, "wrong password 123");
            await ProblemResponse.AssertProblemAsync(failed, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }

        // Locked: even the correct password fails (and looks like any other failure).
        using (var locked = await ApiTestHost.LoginAsync(client, email))
        {
            await ProblemResponse.AssertProblemAsync(locked, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }

        var actions = (await _host.AuditEventsAsync(userId)).Select(e => e.Action).ToList();
        Assert.Equal([.. Enumerable.Repeat("user.login_failed", 4), "user.locked_out", "user.login_failed"], actions);
        Assert.NotNull((await _host.FindUserAsync(email)).LockoutEnd);

        await ResetPasswordAsync(client, email, "brand new password");

        using var login = await ApiTestHost.LoginAsync(client, email, "brand new password");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Signed_in_post_without_csrf_header_is_rejected_and_keeps_the_session()
    {
        var email = ApiTestHost.UniqueEmail();
        await _host.CreateUserAsync(email);
        using var client = await _host.SignedInClientAsync(email);
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        using var logout = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null, Ct);

        await ProblemResponse.AssertProblemAsync(logout, HttpStatusCode.Forbidden, ErrorCodes.CsrfHeaderMissing);
        Assert.False(logout.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email);
        using var client = await _host.SignedInClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);

        using var logout = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-scal=;", StringComparison.Ordinal));
        using var me = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(["user.login_succeeded", "user.logged_out"], (await _host.AuditEventsAsync(userId)).Select(e => e.Action));
    }

    [Fact]
    public async Task Confirmation_email_can_be_resent_and_bad_tokens_are_rejected()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email, emailConfirmed: false);
        using var client = await _host.SignedInClientAsync(email);

        using var resend = await client.PostAsync(new Uri("/api/v1/auth/confirm-email/resend", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, resend.StatusCode);
        var (_, linkUserId, token) = ApiTestHost.LinkIn(await _host.SingleEmailToAsync(email));
        Assert.Equal(userId, linkUserId);

        using var tampered = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token = token[..^4] + "AAAA" }, Ct);
        await ProblemResponse.AssertProblemAsync(tampered, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        using var otherUser = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId = Guid.CreateVersion7(), token }, Ct);
        await ProblemResponse.AssertProblemAsync(otherUser, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);

        using var confirm = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        // Already confirmed: resending is a no-op.
        using var again = await client.PostAsync(new Uri("/api/v1/auth/confirm-email/resend", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Single(await _host.EmailsToAsync(email));
    }

    [Fact]
    public async Task Password_reset_end_to_end_through_the_queued_email()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email, emailConfirmed: false);
        using var client = _host.CreateClient();

        var (path, token) = await RequestResetAsync(client, email);
        Assert.Equal("/reset-password", path);

        using (var weak = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = "short" }, Ct))
        {
            var problem = await ProblemResponse.AssertProblemAsync(weak, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
            Assert.NotNull(problem["errors"]!["newPassword"]);
        }

        using (var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = "brand new password" }, Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        // Single use: the reset changed the security stamp the token was bound to.
        using (var reuse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = "another new password" }, Ct))
        {
            await ProblemResponse.AssertProblemAsync(reuse, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        }

        using (var old = await ApiTestHost.LoginAsync(client, email))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        }

        using var login = await ApiTestHost.LoginAsync(client, email, "brand new password");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((bool)JsonNode.Parse(await login.Content.ReadAsStringAsync(Ct))!["user"]!["emailVerified"]!, "The reset link proves mailbox ownership.");
        var actions = (await _host.AuditEventsAsync(userId)).Select(e => e.Action).ToList();
        Assert.Contains("user.password_reset_requested", actions);
        Assert.Contains("user.password_reset", actions);
    }

    [Fact]
    public async Task Forgot_password_does_not_reveal_whether_an_account_exists()
    {
        using var client = _host.CreateClient();
        var unknown = ApiTestHost.UniqueEmail();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = unknown }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await _host.EmailsToAsync(unknown));
    }

    private async Task<(string Path, string Token)> RequestResetAsync(HttpClient client, string email)
    {
        using var forgot = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var mail = (await _host.EmailsToAsync(email))[^1];
        Assert.Equal("Reset your password", mail.Subject);
        var (path, _, token) = ApiTestHost.LinkIn(mail);
        return (path, token);
    }

    private async Task ResetPasswordAsync(HttpClient client, string email, string newPassword)
    {
        var (_, token) = await RequestResetAsync(client, email);
        var userId = (await _host.FindUserAsync(email)).Id;
        using var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { userId, token, newPassword }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }
}
