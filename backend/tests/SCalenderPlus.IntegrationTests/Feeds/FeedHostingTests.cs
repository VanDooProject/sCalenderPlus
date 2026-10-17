using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Infrastructure.Hosting;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Feeds;

/// <summary>
/// Hosting concerns of feeds (ical-caldav.md §1.1, §1.4): 60 requests per hour per token (burst 10) and no feed
/// secret in any log entry — messages, structured properties or scopes, at every level — nor in trace spans.
/// The reverse proxy's side is checked by deploy/caddy/test-log-redaction.sh (CI job docker).
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class FeedHostingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_default_feed_limit_is_60_per_hour_with_bursts_of_10()
    {
        var feed = new RateLimitingOptions().Feed;

        Assert.Equal(10, feed.TokenLimit);
        Assert.Equal(60, feed.TokensPerPeriod * (TimeSpan.FromHours(1) / feed.ReplenishmentPeriod));
    }

    [Fact]
    public async Task Feeds_are_rate_limited_per_token_with_retry_after()
    {
        await using var host = await ApiTestHost.StartAsync(postgres, s =>
        {
            s["RateLimiting:Feed:TokenLimit"] = "10";
            s["RateLimiting:Feed:TokensPerPeriod"] = "1";
            s["RateLimiting:Feed:ReplenishmentPeriod"] = "00:01:00";
        });
        var email = ApiTestHost.UniqueEmail();
        await host.CreateUserAsync(email);
        using var mia = await host.SignedInClientAsync(email);
        var path = FeedApi.PathOf(await mia.CreateFeedTokenAsync());
        var other = FeedApi.PathOf(await mia.CreateFeedTokenAsync());

        for (var i = 0; i < 10; i++)
        {
            using var allowed = await host.GetFeedAsync(path);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await host.GetFeedAsync(path);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = System.Text.Json.Nodes.JsonNode.Parse(await limited.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(ErrorCodes.RateLimited, (string?)problem["code"]);
        Assert.Equal(SecretRedaction.Replacement, (string?)problem["instance"]); // the problem does not echo the secret path
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        using var unknown = await host.GetFeedAsync("/ical/v1/" + new string('x', 43) + ".ics"); // unknown tokens have their own bucket
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var otherToken = await host.GetFeedAsync(other); // each token has its own budget
        Assert.Equal(HttpStatusCode.OK, otherToken.StatusCode);
    }

    [Fact]
    public async Task Every_spelling_of_a_feed_url_shares_the_token_s_bucket()
    {
        await using var host = await ApiTestHost.StartAsync(postgres, s =>
        {
            s["RateLimiting:Feed:TokenLimit"] = "3";
            s["RateLimiting:Feed:TokensPerPeriod"] = "1";
            s["RateLimiting:Feed:ReplenishmentPeriod"] = "01:00:00";
        });
        var email = ApiTestHost.UniqueEmail();
        await host.CreateUserAsync(email);
        using var mia = await host.SignedInClientAsync(email);
        var secret = (string)(await mia.CreateFeedTokenAsync())["token"]!;

        // The literal segments match in any case and with a trailing slash; the token decides the bucket.
        foreach (var spelling in new[] { $"/ical/v1/{secret}.ics", $"/ICAL/V1/{secret}.ICS", $"/ical/v1/{secret}.ics/" })
        {
            using var allowed = await host.GetFeedAsync(spelling);
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, $"{spelling}: {(int)allowed.StatusCode}");
        }

        using var limited = await host.GetFeedAsync($"/Ical/v1/{secret}.ics");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // A share link with the same secret text would be another bucket (u: and s: partitions).
        Assert.NotEqual(PartitionKey($"/ical/v1/{secret}.ics", "GetFeed", secret), PartitionKey($"/ical/v1/s/{secret}.ics", "GetShareLinkFeed", secret));
    }

    [Fact]
    public async Task Feed_requests_are_limited_per_client_ip_across_tokens()
    {
        await using var host = await ApiTestHost.StartAsync(
            postgres,
            s =>
            {
                s["RateLimiting:FeedPerClient:PermitLimit"] = "3";
                s["RateLimiting:FeedPerClient:Window"] = "01:00:00";
            },
            s => TestPipeline.Add(s, new Dictionary<string, RequestDelegate>(StringComparer.Ordinal)));
        Assert.Equal(600, new RateLimitingOptions().FeedPerClient.PermitLimit); // production default: 600 per hour
        Assert.Equal(TimeSpan.FromHours(1), new RateLimitingOptions().FeedPerClient.Window);

        // Guessing: every request names another unknown token (each has a fresh per-token bucket).
        for (var i = 0; i < 3; i++)
        {
            using var guess = await FeedFromAsync(host, "203.0.113.9", $"/ical/v1/{Guid.NewGuid():N}{i:00000000000}.ics");
            Assert.Equal(HttpStatusCode.NotFound, guess.StatusCode);
        }

        using var limited = await FeedFromAsync(host, "203.0.113.9", $"/ical/v1/s/{Guid.NewGuid():N}00000000000.ics");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = System.Text.Json.Nodes.JsonNode.Parse(await limited.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(ErrorCodes.RateLimited, (string?)problem["code"]);
        Assert.Equal(SecretRedaction.Replacement, (string?)problem["instance"]);
        Assert.InRange(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture), 1, 3600);

        // The address comes from the trusted forwarded headers; IPv6 clients are grouped by /64.
        using var other = await FeedFromAsync(host, "198.51.100.23", $"/ical/v1/{new string('x', 43)}.ics");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        for (var i = 0; i < 3; i++)
        {
            using var v6 = await FeedFromAsync(host, $"2001:db8:1:2::{i + 1:x}", $"/ical/v1/{new string('y', 43)}.ics");
            Assert.Equal(HttpStatusCode.NotFound, v6.StatusCode);
        }

        using var sameNetwork = await FeedFromAsync(host, "2001:db8:1:2:ffff::1", $"/ical/v1/{new string('z', 43)}.ics");
        Assert.Equal(HttpStatusCode.TooManyRequests, sameNetwork.StatusCode);

        // Other endpoints are not counted against the feed limit.
        using var client = host.CreateClient();
        using var api = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task No_feed_secret_reaches_a_log_entry_or_a_trace()
    {
        var logs = new CapturingLoggerProvider();
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        await using var host = await ApiTestHost.StartAsync(
            postgres,
            s =>
            {
                s["Logging:LogLevel:Default"] = "Trace";
                s["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace";
            },
            s => s.AddSingleton<ILoggerProvider>(logs));
        var email = ApiTestHost.UniqueEmail();
        await host.CreateUserAsync(email);
        using var mia = await host.SignedInClientAsync(email);
        var issued = await mia.CreateFeedTokenAsync();
        var secret = (string)issued["token"]!;
        var calendar = await Calendars.CalendarApi.CreateCalendarAsync(mia);
        using (var link = await Groups.GroupApi.SendJsonAsync(mia, HttpMethod.Post, $"/api/v1/calendars/{calendar}/share-links", new { level = "read" }))
        {
            Assert.Equal(HttpStatusCode.Created, link.StatusCode);
            var linkSecret = (string)(await Groups.GroupApi.JsonAsync(link))["token"]!;
            using var shareFeed = await host.GetFeedAsync($"/ical/v1/s/{linkSecret}.ics");
            Assert.Equal(HttpStatusCode.OK, shareFeed.StatusCode);
            logs.Secrets.Add(linkSecret);
        }

        logs.Secrets.Add(secret);
        using var feed = await host.GetFeedAsync(FeedApi.PathOf(issued));
        using var notModified = await host.GetFeedAsync(FeedApi.PathOf(issued), ifNoneMatch: feed.Headers.ETag!.ToString());
        using var percentEncoded = await host.GetFeedAsync($"/%69cal/v1/{secret}.ics");
        foreach (var spelling in new[]
        {
            $"/%69%63%61%6C/v1/{secret}.ics",
            $"/%49%43%41%4c/v1/{secret}.ics",
            $"/ICAL/V1/{secret}.ics",
            $"/ical%2Fv1%2F{secret}.ics",
            $"/ical%5Cv1%5C{secret}.ics",
            $"%2Fical%2Fv1%2F{secret}.ics",
            $"/api/v1/x/..%2F..%2Fical/v1/{secret}.ics",
        })
        {
            using var _ = await host.GetFeedAsync(spelling);
        }

        using var unknown = await host.GetFeedAsync("/ical/v1/" + new string('q', 43) + ".ics?x=" + secret);
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        // The hosting log of every request was captured (redacted), and nothing anywhere holds a secret.
        Assert.Contains(logs.Entries, e => e.Contains(SecretRedaction.Replacement, StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Contains("Request starting", StringComparison.Ordinal));
        foreach (var known in logs.Secrets)
        {
            Assert.DoesNotContain(logs.Entries, e => e.Contains(known, StringComparison.Ordinal));
            Assert.DoesNotContain(spans, s => s.TagObjects.Any(t => t.Value?.ToString()?.Contains(known, StringComparison.Ordinal) == true) || s.DisplayName.Contains(known, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("GET /ical/v1/abc_DEF-123.ics HTTP/1.1", "GET /ical/[REDACTED] HTTP/1.1")]
    [InlineData("https://app.example.test/ical/v1/s/abc.ics?x=1", "https://app.example.test/ical/[REDACTED]")]
    [InlineData("RequestPath:/ICAL/v1/abc.ics RequestId:1", "RequestPath:/ical/[REDACTED] RequestId:1")]
    [InlineData("/api/v1/feed-tokens", "/api/v1/feed-tokens")]
    [InlineData("GET /%69%63%61%6C/v1/abc.ics HTTP/1.1", "GET /ical/[REDACTED] HTTP/1.1")]
    [InlineData("GET /%49cAl/v1/abc.ics", "GET /ical/[REDACTED]")]
    [InlineData("/ical%2Fv1%2Fabc.ics", "/ical/[REDACTED]")]
    [InlineData("/ical%2fv1%2fabc.ics", "/ical/[REDACTED]")]
    [InlineData("%2Fical%2Fv1%2Fabc.ics", "/ical/[REDACTED]")]
    [InlineData(@"C:\x\ical\v1\abc.ics", "C:\\x/ical/[REDACTED]")]
    [InlineData("/ical%5Cv1%5Cabc.ics", "/ical/[REDACTED]")]
    [InlineData("%5Cical%5Cv1%5Cabc.ics", "/ical/[REDACTED]")]
    [InlineData("/api/v1/calendars?q=icalendar", "/api/v1/calendars?q=icalendar")]
    [InlineData("/icalendar/x", "/icalendar/x")]
    public void Redaction_masks_everything_from_an_ical_segment_in_any_spelling(string input, string expected) =>
        Assert.Equal(expected, SecretRedaction.Redact(input));

    private static string PartitionKey(string path, string endpointName, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.RouteValues["token"] = token;
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new Microsoft.AspNetCore.Routing.EndpointNameMetadata(endpointName)), endpointName));
        return RateLimitingSetup.FeedKey(context);
    }

    private static async Task<HttpResponseMessage> FeedFromAsync(ApiTestHost host, string clientAddress, string path)
    {
        using var client = host.CreateClient(csrfHeader: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(TestPipeline.RemoteIpHeader, "127.0.0.1"); // the trusted proxy (web)
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>Records every log entry as the JSON console formatter would see it: message, properties and scopes.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public ConcurrentQueue<string> Entries { get; } = new();

        public ConcurrentBag<string> Secrets { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => provider._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var entry = new System.Text.StringBuilder(category).Append(' ').Append(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        entry.Append(' ').Append(key).Append('=').Append(value);
                    }
                }

                provider._scopes?.ForEachScope(
                    (scope, builder) =>
                    {
                        builder.Append(" scope:").Append(scope);
                        if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                        {
                            foreach (var (key, value) in scopePairs)
                            {
                                builder.Append(' ').Append(key).Append('=').Append(value);
                            }
                        }
                    },
                    entry);
                provider.Entries.Enqueue(entry.Append(' ').Append(exception).ToString());
            }
        }
    }
}
