using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using SCalenderPlus.Application.Events;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using Calendar = SCalenderPlus.Core.Calendars.Calendar;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// Issue #45 performance acceptance: 100,000 events in 20 calendars over four years; the caller sees 10 of the
/// calendars (≈ 1,000 events per month). The window query must use the GiST index <c>(calendar_id, occurs_range)</c>
/// (asserted on the real query's EXPLAIN — a hard check, independent of machine speed) and answer a month
/// within p95 &lt; 150 ms end to end (HTTP, engine, JSON).
/// <para>
/// Timing: asserted as such locally; on CI (<c>CI=true</c>, shared and noisy runners whose speed varies several
/// times between runs) the p95 is logged and only a generous ceiling is asserted, which still catches a lost index
/// (a sequential scan over 100k rows plus per-row work is well beyond it) without making the build flaky.
/// </para>
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventWindowPerformanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int EventCount = 100_000;
    private const int CalendarCount = 20;
    private const int VisibleCalendars = 10;
    private const double TargetP95Ms = 150;
    private const double CiCeilingP95Ms = 1000;

    private readonly List<Guid> _calendars = [];
    private ApiTestHost _host = null!;
    private HttpClient _viewer = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        var viewerEmail = ApiTestHost.UniqueEmail("viewer");
        var viewerId = await _host.CreateUserAsync(viewerEmail, displayName: "viewer");
        var otherId = await _host.CreateUserAsync(ApiTestHost.UniqueEmail("other"), displayName: "other");
        _viewer = await _host.SignedInClientAsync(viewerEmail);

        // Calendars directly (the plan limit of owned calendars is not the subject here).
        await _host.QueryAsync(async db =>
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            for (var i = 0; i < CalendarCount; i++)
            {
                var calendar = new Calendar
                {
                    Id = Guid.CreateVersion7(),
                    OwnerUserId = i < VisibleCalendars ? viewerId : otherId,
                    Name = $"Calendar {i}",
                    DefaultTimeZone = "Europe/Berlin",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Calendars.Add(calendar);
                _calendars.Add(calendar.Id);
            }

            return await db.SaveChangesAsync(Ct);
        });

        // 100k timed events, one every 21 minutes from 2025-01-01 (≈ 4 years), round robin over the calendars;
        // every tenth is all-day; durations 30–120 minutes.
        var calendars = _calendars.ToArray();
        await _host.ExecuteSqlAsync($"""
            INSERT INTO events (id, calendar_id, uid, creator_user_id, title, status, transparency, categories, all_day,
                                start_local, end_local, start_date, end_date, time_zone, start_utc, end_utc,
                                has_overrides, sequence, created_at, updated_at)
            SELECT id, calendar_id, id::text || '@scalenderplus', {viewerId}, 'Event ' || n, 0, 0, ARRAY[]::text[], all_day,
                   CASE WHEN all_day THEN NULL ELSE start_utc AT TIME ZONE 'Europe/Berlin' END,
                   CASE WHEN all_day THEN NULL ELSE end_utc AT TIME ZONE 'Europe/Berlin' END,
                   CASE WHEN all_day THEN (start_utc AT TIME ZONE 'UTC')::date END,
                   CASE WHEN all_day THEN (start_utc AT TIME ZONE 'UTC')::date + 1 END,
                   CASE WHEN all_day THEN NULL ELSE 'Europe/Berlin' END,
                   CASE WHEN all_day THEN date_trunc('day', start_utc) - interval '14 hours' ELSE start_utc END,
                   CASE WHEN all_day THEN date_trunc('day', start_utc) + interval '38 hours' ELSE end_utc END,
                   false, 0, now(), now()
            FROM (
                SELECT n, gen_random_uuid() AS id, ({calendars}::uuid[])[1 + n % {CalendarCount}] AS calendar_id, n % 10 = 0 AS all_day,
                       timestamptz '2025-01-01 00:00Z' + n * interval '21 minutes' AS start_utc,
                       timestamptz '2025-01-01 00:00Z' + n * interval '21 minutes' + (30 + n % 4 * 30) * interval '1 minute' AS end_utc
                FROM generate_series(0, {EventCount - 1}) AS n
            ) AS seed
            """);
        await _host.ExecuteSqlAsync($"ANALYZE events");
    }

    public async ValueTask DisposeAsync()
    {
        _viewer.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_window_query_uses_the_gist_index()
    {
        var plan = await ExplainAsync([.. _calendars.Take(VisibleCalendars)], Instant.FromUtc(2026, 11, 1, 0, 0), Instant.FromUtc(2026, 12, 1, 0, 0));
        TestContext.Current.TestOutputHelper?.WriteLine(plan);

        Assert.Contains("ix_events_calendar_id_occurs_range", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on events", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_month_answers_within_the_latency_budget()
    {
        var durations = new List<double>();
        for (var run = -5; run < 40; run++)
        {
            // Months of 2025–2028 (warm-up runs first, not measured).
            var month = new LocalDate(2025, 1, 1).PlusMonths((Math.Abs(run) * 7) % 46);
            var from = month.AtMidnight().InUtc().ToInstant();
            var to = month.PlusMonths(1).AtMidnight().InUtc().ToInstant();
            var watch = Stopwatch.StartNew();
            using var response = await _viewer.GetAsync(new Uri($"/api/v1/events?from={from}&to={to}&timeZone=Europe/Berlin", UriKind.Relative), Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            watch.Stop();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            var items = JsonNode.Parse(body)!["items"]!.AsArray();
            Assert.InRange(items.Count, 900, 1200); // the caller's half of ≈ 2,100 events a month
            if (run >= 0)
            {
                durations.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        durations.Sort();
        var p95 = durations[(int)Math.Ceiling(durations.Count * 0.95) - 1];
        var onCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        var report = string.Create(CultureInfo.InvariantCulture, $"Window query, 1 month of {EventCount:N0} events: p50 {durations[durations.Count / 2]:F1} ms, p95 {p95:F1} ms, max {durations[^1]:F1} ms (budget {TargetP95Ms} ms{(onCi ? $", CI ceiling {CiCeilingP95Ms} ms" : string.Empty)})");
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        Console.WriteLine(report);

        Assert.True(p95 < (onCi ? CiCeilingP95Ms : TargetP95Ms), report);
    }

    [Fact]
    public async Task A_large_window_is_capped()
    {
        using var response = await _viewer.GetAsync(new Uri("/api/v1/events?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z", UriKind.Relative), Ct);
        var body = await response.JsonAsync();

        Assert.True((bool)body["truncated"]!);
        var items = body["items"]!.AsArray();
        Assert.Equal(EventQueryService.MaxWindowEvents, items.Count);
        var starts = items.Select(i => (DateTimeOffset?)i!["start"]!["utc"] ?? DateTimeOffset.Parse((string)i!["start"]!["date"]!, CultureInfo.InvariantCulture)).ToList();
        Assert.True(starts[0] < new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)); // the earliest come first
    }

    /// <summary>EXPLAIN of exactly the SQL the query service sends (<see cref="EventQueryService.WindowSql"/>).</summary>
    private async Task<string> ExplainAsync(Guid[] calendarIds, Instant from, Instant to)
    {
        var sql = EventQueryService.WindowSql(calendarIds, from, to, EventQueryService.MaxWindowEvents + 1);
        var arguments = sql.GetArguments();
        var text = string.Format(CultureInfo.InvariantCulture, sql.Format, [.. Enumerable.Range(1, arguments.Length).Select(i => (object)$"${i}")]);
        return await _host.QueryAsync(async db =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, COSTS OFF) " + text, connection);
            foreach (var argument in arguments)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = argument is Instant instant ? instant.ToDateTimeOffset() : argument! });
            }

            var lines = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                lines.Add(reader.GetString(0));
            }

            return string.Join('\n', lines);
        });
    }
}
