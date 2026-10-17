using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;

namespace SCalenderPlus.IntegrationTests.Feeds;

/// <summary>Helpers for feed tests: feed links through the api, feed requests like a calendar app's.</summary>
internal static class FeedApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates a feed link (asserts 201) and returns the response with secret and URLs.</summary>
    public static async Task<JsonNode> CreateFeedTokenAsync(this HttpClient client, string scope = "aggregate", Guid? calendarId = null, string? labelMode = null)
    {
        using var response = await client.SendJsonAsync(HttpMethod.Post, "/api/v1/feed-tokens", new { scope, calendarId, labelMode });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    /// <summary>The path of a feed URL (<c>/ical/v1/….ics</c>).</summary>
    public static string PathOf(JsonNode issued) => new Uri((string)issued["url"]!).AbsolutePath;

    /// <summary>A feed request as a calendar app sends it (no cookies, no CSRF header), optionally conditional.</summary>
    public static async Task<HttpResponseMessage> GetFeedAsync(this ApiTestHost host, string path, string? ifNoneMatch = null, DateTimeOffset? ifModifiedSince = null, string? acceptEncoding = null)
    {
        using var client = host.CreateClient(csrfHeader: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        if (ifModifiedSince is { } since)
        {
            request.Headers.IfModifiedSince = since;
        }

        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        return await client.SendAsync(request, Ct);
    }

    /// <summary>The feed body (asserts 200 and the iCalendar content type), with folded lines joined.</summary>
    public static async Task<string> FeedTextAsync(this ApiTestHost host, string path)
    {
        using var response = await host.GetFeedAsync(path);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        return body.Replace("\r\n ", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A wall clock <paramref name="days"/> from today at <paramref name="hour"/>:00 (feeds start 90 days back, so fixed dates would age out).</summary>
    public static string Soon(int days, int hour = 18) =>
        SystemClock.Instance.GetCurrentInstant().InUtc().Date.PlusDays(days).At(new LocalTime(hour, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", null);

    /// <summary>The VEVENT blocks of a feed whose SUMMARY line contains <paramref name="summary"/>.</summary>
    public static IReadOnlyList<string> Events(string feed, string? summary = null) =>
        [.. feed.Split("BEGIN:VEVENT\r\n").Skip(1).Select(e => e[..e.IndexOf("END:VEVENT", StringComparison.Ordinal)])
            .Where(e => summary is null || e.Contains("SUMMARY:" + summary, StringComparison.Ordinal))];
}

/// <summary>Records the SQL of every command while <see cref="Recording"/> (to prove a 304 loads no events).</summary>
internal sealed class CommandRecorder : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();

    public bool Recording { get; set; }

    /// <summary>Commands that read the <c>events</c> table (EF queries alias it <c>FROM events AS</c>, the window SQL <c>FROM events</c>).</summary>
    public IReadOnlyList<string> EventQueries =>
        [.. Commands.Where(c => System.Text.RegularExpressions.Regex.IsMatch(c, @"\b(FROM|JOIN)\s+events\b"))];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        if (Recording)
        {
            Commands.Enqueue(command.CommandText);
        }
    }
}
