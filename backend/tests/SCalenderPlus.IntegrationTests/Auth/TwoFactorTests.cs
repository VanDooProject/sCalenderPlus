using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Infrastructure.Identity;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>Issue #31: TOTP two-factor authentication with single-use, hashed recovery codes.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class TwoFactorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Enabling_2fa_makes_login_require_a_code()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email);
        using var client = await _host.SignedInClientAsync(email);

        var status = await GetJsonAsync(client, "/api/v1/me/two-factor");
        Assert.False((bool)status["enabled"]!);

        var setup = await PostJsonAsync(client, "/api/v1/me/two-factor/setup", null, HttpStatusCode.OK);
        var secret = (string)setup["sharedKey"]!;
        Assert.Equal(
            $"otpauth://totp/sCalenderPlus:{Uri.EscapeDataString(email)}?secret={secret}&issuer=sCalenderPlus&digits=6",
            (string?)setup["authenticatorUri"]);

        using (var wrong = await client.PostAsJsonAsync("/api/v1/me/two-factor/enable", new { code = "000000" }, Ct))
        {
            var problem = await ProblemResponse.AssertProblemAsync(wrong, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
            Assert.NotNull(problem["errors"]!["code"]);
        }

        var enabled = await PostJsonAsync(client, "/api/v1/me/two-factor/enable", new { code = Totp.Compute(secret) }, HttpStatusCode.OK);
        var codes = enabled["recoveryCodes"]!.AsArray().Select(c => (string)c!).ToList();
        Assert.Equal(10, codes.Distinct().Count());

        // The session that enabled 2FA stays signed in (its cookie is re-issued).
        status = await GetJsonAsync(client, "/api/v1/me/two-factor");
        Assert.True((bool)status["enabled"]!);
        Assert.Equal(10, (int)status["recoveryCodesLeft"]!);
        Assert.True((bool)(await GetJsonAsync(client, "/api/v1/me"))["twoFactorEnabled"]!);

        using var second = _host.CreateClient();
        using (var password = await ApiTestHost.LoginAsync(second, email))
        {
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            var body = JsonNode.Parse(await password.Content.ReadAsStringAsync(Ct))!;
            Assert.True((bool)body["twoFactorRequired"]!);
            Assert.Null(body["user"]);
            var cookies = password.Headers.GetValues("Set-Cookie").ToList();
            Assert.DoesNotContain(cookies, c => c.StartsWith("__Host-scal=", StringComparison.Ordinal));
            Assert.Contains(cookies, c => c.StartsWith("__Host-scal-2fa=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        }

        // Password alone is no session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);

        using (var wrongCode = await second.PostAsJsonAsync("/api/v1/auth/login/2fa", new { code = "000000" }, Ct))
        {
            await ProblemResponse.AssertProblemAsync(wrongCode, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }

        var loggedIn = await PostJsonAsync(second, "/api/v1/auth/login/2fa", new { code = Totp.Compute(secret) }, HttpStatusCode.OK);
        Assert.Equal(userId.ToString(), (string?)loggedIn["user"]!["id"]);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);

        var actions = (await _host.AuditEventsAsync(userId)).Select(e => e.Action).ToList();
        Assert.Equal(["user.login_succeeded", "user.two_factor_enabled", "user.login_failed", "user.login_succeeded"], actions);
    }

    [Fact]
    public async Task A_recovery_code_works_exactly_once()
    {
        var (email, _, codes) = await CreateTwoFactorUserAsync();

        using (var first = await SecondStepAsync(email, new { recoveryCode = codes[0] }))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var reused = await SecondStepAsync(email, new { recoveryCode = codes[0] }))
        {
            await ProblemResponse.AssertProblemAsync(reused, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }

        // Case, spaces and the dash don't matter.
        using (var other = await SecondStepAsync(email, new { recoveryCode = " " + codes[1].Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() }))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }

        using var client = await SignedInTwoFactorClientAsync(email, codes[2]);
        Assert.Equal(7, (int)(await GetJsonAsync(client, "/api/v1/me/two-factor"))["recoveryCodesLeft"]!);
    }

    [Fact]
    public async Task Recovery_codes_are_stored_hashed()
    {
        var (_, userId, codes) = await CreateTwoFactorUserAsync();

        await using var scope = _host.Api.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<IdentityUserToken<Guid>>().AsNoTracking()
            .Where(t => t.UserId == userId && t.Name == "RecoveryCodes").Select(t => t.Value).SingleAsync(Ct);

        Assert.Equal(10, stored!.Split(';').Length);
        Assert.All(codes, code => Assert.DoesNotContain(code, stored, StringComparison.OrdinalIgnoreCase));
        Assert.All(codes, code => Assert.DoesNotContain(code.Replace("-", string.Empty, StringComparison.Ordinal), stored, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Second_step_without_a_pending_login_is_401()
    {
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login/2fa", new { code = "123456" }, Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Disabling_2fa_needs_the_password_or_a_code()
    {
        var (email, userId, codes) = await CreateTwoFactorUserAsync();
        using var client = await SignedInTwoFactorClientAsync(email, codes[0]);

        using (var wrong = await client.PostAsJsonAsync("/api/v1/me/two-factor/disable", new { password = "wrong password 123" }, Ct))
        {
            await ProblemResponse.AssertProblemAsync(wrong, HttpStatusCode.Forbidden, ErrorCodes.ReauthenticationFailed);
        }

        using (var neither = await client.PostAsJsonAsync("/api/v1/me/two-factor/disable", new { }, Ct))
        {
            await ProblemResponse.AssertProblemAsync(neither, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        using (var disable = await client.PostAsJsonAsync("/api/v1/me/two-factor/disable", new { password = ApiTestHost.Password }, Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        }

        var status = await GetJsonAsync(client, "/api/v1/me/two-factor");
        Assert.False((bool)status["enabled"]!);
        Assert.Equal(0, (int)status["recoveryCodesLeft"]!);
        using var login = await ApiTestHost.LoginAsync(_host.CreateClient(), email);
        Assert.False((bool)JsonNode.Parse(await login.Content.ReadAsStringAsync(Ct))!["twoFactorRequired"]!);
        Assert.Contains("user.two_factor_disabled", (await _host.AuditEventsAsync(userId)).Select(e => e.Action));
    }

    [Fact]
    public async Task Regenerated_recovery_codes_replace_the_old_ones()
    {
        var (email, _, codes) = await CreateTwoFactorUserAsync();
        using var client = await SignedInTwoFactorClientAsync(email, codes[0]);
        var secret = await AuthenticatorKeyAsync(email);

        var regenerated = await PostJsonAsync(client, "/api/v1/me/two-factor/recovery-codes", new { code = Totp.Compute(secret) }, HttpStatusCode.OK);
        var fresh = regenerated["recoveryCodes"]!.AsArray().Select(c => (string)c!).ToList();

        Assert.Equal(10, fresh.Count);
        using (var old = await SecondStepAsync(email, new { recoveryCode = codes[1] }))
        {
            await ProblemResponse.AssertProblemAsync(old, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }

        using var newCode = await SecondStepAsync(email, new { recoveryCode = fresh[0] });
        Assert.Equal(HttpStatusCode.OK, newCode.StatusCode);
    }

    [Fact]
    public async Task Setup_is_refused_while_2fa_is_enabled()
    {
        var (email, _, codes) = await CreateTwoFactorUserAsync();
        using var client = await SignedInTwoFactorClientAsync(email, codes[0]);

        using var response = await client.PostAsync(new Uri("/api/v1/me/two-factor/setup", UriKind.Relative), null, Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    /// <summary>A user with 2FA enabled through the API; returns its recovery codes.</summary>
    private async Task<(string Email, Guid UserId, IReadOnlyList<string> Codes)> CreateTwoFactorUserAsync()
    {
        var email = ApiTestHost.UniqueEmail();
        var userId = await _host.CreateUserAsync(email);
        using var client = await _host.SignedInClientAsync(email);
        var secret = (string)(await PostJsonAsync(client, "/api/v1/me/two-factor/setup", null, HttpStatusCode.OK))["sharedKey"]!;
        var enabled = await PostJsonAsync(client, "/api/v1/me/two-factor/enable", new { code = Totp.Compute(secret) }, HttpStatusCode.OK);
        return (email, userId, enabled["recoveryCodes"]!.AsArray().Select(c => (string)c!).ToList());
    }

    private async Task<HttpResponseMessage> SecondStepAsync(string email, object secondFactor)
    {
        using var client = _host.CreateClient();
        using var password = await ApiTestHost.LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        return await client.PostAsJsonAsync("/api/v1/auth/login/2fa", secondFactor, Ct);
    }

    private async Task<HttpClient> SignedInTwoFactorClientAsync(string email, string recoveryCode)
    {
        var client = _host.CreateClient();
        using var password = await ApiTestHost.LoginAsync(client, email);
        using var second = await client.PostAsJsonAsync("/api/v1/auth/login/2fa", new { recoveryCode }, Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        return client;
    }

    private async Task<string> AuthenticatorKeyAsync(string email)
    {
        await using var scope = _host.Api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!))!;
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path) =>
        JsonNode.Parse(await client.GetStringAsync(new Uri(path, UriKind.Relative), Ct))!;

    private static async Task<JsonNode> PostJsonAsync(HttpClient client, string path, object? body, HttpStatusCode expected)
    {
        using var response = body is null
            ? await client.PostAsync(new Uri(path, UriKind.Relative), null, Ct)
            : await client.PostAsJsonAsync(path, body, Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{path}: {(int)response.StatusCode} {raw}");
        return JsonNode.Parse(raw)!;
    }
}
