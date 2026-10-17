using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>
/// Keeps feed secrets out of the logs (ical-caldav.md §1.1): iCal feed URLs carry the token in the path
/// (<c>/ical/v1/{token}.ics</c>, <c>/ical/v1/s/{token}.ics</c>), and ASP.NET Core logs request paths — in messages
/// ("Request starting …"), in structured properties and in the request scope (<c>RequestPath</c>) that the JSON
/// console formatter writes with every entry of a request. Everything after <c>/ical/</c> is replaced by
/// <see cref="Replacement"/> in all of them, for every provider (console, OpenTelemetry, and any added later or by
/// tests), by decorating the logger factory. The reverse proxy masks the same paths in its own logs
/// (deploy/caddy/web.Caddyfile).
/// </summary>
public static partial class SecretRedaction
{
    public const string Replacement = "/ical/[REDACTED]";

    /// <summary>Whether <paramref name="value"/> may contain a feed secret.</summary>
    public static bool Contains(string? value) => value is not null && value.Contains("/ical/", StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="value"/> with everything after <c>/ical/</c> (up to whitespace or a quote) masked.</summary>
    public static string Redact(string value) => Contains(value) ? FeedPath().Replace(value, Replacement) : value;

    /// <summary>Decorates the logger factory so every logger redacts messages, properties and scopes.</summary>
    public static IServiceCollection AddLogRedaction(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(sp => new RedactingLoggerFactory(new LoggerFactory(
            sp.GetServices<ILoggerProvider>(),
            sp.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
            sp.GetService<IOptions<LoggerFactoryOptions>>(),
            sp.GetService<IExternalScopeProvider>()))));
        return services;
    }

    [GeneratedRegex("""/ical/[^\s"'<>]*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeedPath();

    /// <summary>Redacts a log state or scope: structured properties and the formatted text.</summary>
    private static object? RedactState(object? state, string? text)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            var hit = Contains(text);
            for (var i = 0; i < pairs.Count && !hit; i++)
            {
                hit = Contains(pairs[i].Value?.ToString());
            }

            return hit ? new RedactedState(pairs, Redact(text ?? string.Empty)) : state;
        }

        return Contains(text) ? Redact(text!) : state;
    }

    private sealed class RedactingLoggerFactory(LoggerFactory inner) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new RedactingLogger(inner.CreateLogger(categoryName));

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose() => inner.Dispose();
    }

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            RedactState(state, state.ToString()) is { } redacted && !ReferenceEquals(redacted, state)
                ? inner.BeginScope(redacted)
                : inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!inner.IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var redacted = RedactState(state, message);
            if (ReferenceEquals(redacted, state))
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
            else
            {
                inner.Log(logLevel, eventId, redacted, exception, static (s, _) => s?.ToString() ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// A structured state whose string values and text are redacted (the JSON formatter writes both). The hosting
    /// logs carry the query string of the same request in a property of its own: it goes too.
    /// </summary>
    private sealed class RedactedState(IReadOnlyList<KeyValuePair<string, object?>> pairs, string text) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _pairs =
        [
            .. pairs.Select(p => Contains(p.Value?.ToString())
                ? new KeyValuePair<string, object?>(p.Key, Redact(p.Value!.ToString()!))
                : p.Key == "QueryString" && !string.IsNullOrEmpty(p.Value?.ToString())
                    ? new KeyValuePair<string, object?>(p.Key, "?[REDACTED]")
                    : p),
        ];

        public int Count => _pairs.Length;

        public KeyValuePair<string, object?> this[int index] => _pairs[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_pairs).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => text;
    }
}
