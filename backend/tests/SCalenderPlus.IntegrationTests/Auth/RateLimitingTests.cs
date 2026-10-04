using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>Issue #30: per-IP limits on auth and sign-up, per-session abuse limit, token plan limits, disposable emails.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class RateLimitingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_is_limited_per_client_ip_with_429_problem_and_retry_after()
    {
        await using var host = await StartAsync(s =>
        {
            s["RateLimiting:Auth:PermitLimit"] = "3";
            s["RateLimiting:Auth:Window"] = "00:01:00";
        });
        using var client = host.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var allowed = await LoginFromAsync(client, "203.0.113.7");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await LoginFromAsync(client, "203.0.113.7");
        await ProblemResponse.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);

        // The client address comes from the trusted forwarded headers: another client has its own budget.
        using var otherClient = await LoginFromAsync(client, "198.51.100.23");
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
    }

    [Fact]
    public async Task Sign_up_is_limited_per_client_ip()
    {
        await using var host = await StartAsync(s => s["RateLimiting:SignUp:PermitLimit"] = "2");
        using var client = host.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await RegisterAsync(client, ApiTestHost.UniqueEmail());
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var limited = await RegisterAsync(client, ApiTestHost.UniqueEmail());
        await ProblemResponse.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) > 60, "Default sign-up window is one hour.");
    }

    [Fact]
    public async Task Invite_acceptance_is_limited_per_user()
    {
        await using var host = await StartAsync(s => s["RateLimiting:InviteAccept:PermitLimit"] = "2");
        var email = ApiTestHost.UniqueEmail();
        await host.CreateUserAsync(email);
        using var client = await host.SignedInClientAsync(email);

        for (var i = 0; i < 2; i++)
        {
            using var guess = await client.PostAsJsonAsync("/api/v1/invites/accept", new { token = "guess" + i }, TestContext.Current.CancellationToken);
            await ProblemResponse.AssertProblemAsync(guess, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        }

        using var limited = await client.PostAsJsonAsync("/api/v1/invites/accept", new { token = "guess" }, TestContext.Current.CancellationToken);
        await ProblemResponse.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        Assert.True(limited.Headers.Contains("Retry-After"));

        // Another user has their own budget.
        var other = ApiTestHost.UniqueEmail("other");
        await host.CreateUserAsync(other);
        using var otherClient = await host.SignedInClientAsync(other);
        using var allowed = await otherClient.PostAsJsonAsync("/api/v1/invites/accept", new { token = "guess" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
    }

    [Fact]
    public async Task Invite_previews_are_limited_per_client_ip()
    {
        await using var host = await StartAsync(s => s["RateLimiting:InvitePreview:PermitLimit"] = "2");
        using var client = host.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var guess = await PreviewFromAsync(client, "203.0.113.9", "guess" + i);
            await ProblemResponse.AssertProblemAsync(guess, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        }

        using var limited = await PreviewFromAsync(client, "203.0.113.9", "guess");
        await ProblemResponse.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        Assert.True(limited.Headers.Contains("Retry-After"));

        using var otherClient = await PreviewFromAsync(client, "198.51.100.24", "guess");
        Assert.Equal(HttpStatusCode.BadRequest, otherClient.StatusCode);
    }

    [Fact]
    public async Task Session_abuse_limit_applies_per_signed_in_user()
    {
        await using var host = await StartAsync(s => s["RateLimiting:Session:PermitLimit"] = "5");
        var mia = ApiTestHost.UniqueEmail("mia");
        var vic = ApiTestHost.UniqueEmail("vic");
        await host.CreateUserAsync(mia);
        await host.CreateUserAsync(vic);
        using var miaClient = await host.SignedInClientAsync(mia);
        using var vicClient = await host.SignedInClientAsync(vic);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await miaClient.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);
        }

        using var limited = await miaClient.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, (await vicClient.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Normal_ui_navigation_never_hits_the_default_session_limit()
    {
        await using var host = await StartAsync(); // production defaults: 600 requests per minute and user
        var email = ApiTestHost.UniqueEmail();
        await host.CreateUserAsync(email);
        using var client = await host.SignedInClientAsync(email);

        // A busy minute in the web app: 60 navigations, each loading about five resources (the shell's /me plus
        // the view's queries), sent back to back — far denser than a person clicks.
        for (var navigation = 0; navigation < 60; navigation++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("throwaway@mailinator.com")]
    [InlineData("someone@eu.mailinator.com")]
    [InlineData("SOMEONE@YOPMAIL.COM")]
    [InlineData("mia@blocked.example")]
    public async Task Sign_up_with_a_disposable_or_blocked_email_domain_is_422(string email)
    {
        await using var host = await StartAsync(s => s["SignUp:BlockedEmailDomains"] = "blocked.example, other.example");
        using var client = host.CreateClient();

        using var response = await RegisterAsync(client, email);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.EmailDomainNotAllowed);
        Assert.NotNull(problem["errors"]!["email"]);
        Assert.Empty(await host.EmailsToAsync(email));
    }

    [Fact]
    public void Api_tokens_are_limited_per_token_by_plan()
    {
        var settings = new RateLimitingOptions();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(RateLimitingSetup.TokenIdClaim, "t1"), new Claim(RateLimitingSetup.PlanClaim, "pro")],
            RateLimitingSetup.ApiTokenAuthenticationType);

        var partition = RateLimitingSetup.GlobalPartition(new ClaimsPrincipal(identity), settings);

        Assert.Equal("token:t1", partition.PartitionKey);
        using var limiter = partition.Factory(partition.PartitionKey);
        Assert.True(limiter.AttemptAcquire(600).IsAcquired);
        Assert.False(limiter.AttemptAcquire(1).IsAcquired);
    }

    [Fact]
    public void Cookie_sessions_are_limited_per_user_and_anonymous_requests_globally_not_at_all()
    {
        var settings = new RateLimitingOptions();
        var session = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "Identity.Application"));

        Assert.Equal("session:u1", RateLimitingSetup.GlobalPartition(session, settings).PartitionKey);
        var anonymous = RateLimitingSetup.GlobalPartition(new ClaimsPrincipal(new ClaimsIdentity()), settings);
        using var noLimiter = anonymous.Factory(anonymous.PartitionKey);
        Assert.True(noLimiter.AttemptAcquire(1_000_000).IsAcquired);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData(null, "unknown")]
    public void Client_key_groups_ipv6_by_its_64_bit_prefix(string? address, string expected) =>
        Assert.Equal(expected, RateLimitingSetup.ClientKey(address is null ? null : IPAddress.Parse(address)));

    private async Task<ApiTestHost> StartAsync(Action<Dictionary<string, string?>>? configure = null) =>
        await ApiTestHost.StartAsync(
            postgres,
            settings =>
            {
                // Production defaults unless the test sets its own (TestSettings raises the auth limits for other tests).
                settings.Remove("RateLimiting:Auth:PermitLimit");
                settings.Remove("RateLimiting:SignUp:PermitLimit");
                configure?.Invoke(settings);
            },
            services => TestPipeline.Add(services, new Dictionary<string, RequestDelegate>(StringComparer.Ordinal)));

    private static async Task<HttpResponseMessage> PreviewFromAsync(HttpClient client, string clientAddress, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/invites/preview", UriKind.Relative))
        {
            Content = JsonContent.Create(new { token }),
        };
        request.Headers.Add(TestPipeline.RemoteIpHeader, "127.0.0.1"); // the trusted proxy (web)
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> LoginFromAsync(HttpClient client, string clientAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new { email = "nobody@example.test", password = "wrong password 123" }),
        };
        request.Headers.Add(TestPipeline.RemoteIpHeader, "127.0.0.1"); // the trusted proxy (web)
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return await client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = ApiTestHost.Password, displayName = "Mia" }, Ct);
}
