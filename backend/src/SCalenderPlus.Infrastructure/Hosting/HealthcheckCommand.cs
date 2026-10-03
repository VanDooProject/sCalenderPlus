using System.Globalization;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>
/// <c>healthcheck [ready|live] [--url http://host:port]</c>: container health probe for chiseled images
/// (no shell/curl). GETs <c>/health/ready</c> (default) or <c>/health/live</c> on localhost and exits 0 when
/// healthy, 1 otherwise. The target comes from <c>--url</c>, else the first <c>http://</c> entry of
/// <c>ASPNETCORE_URLS</c> (which Kestrel prefers over the ports variables), else <c>ASPNETCORE_HTTP_PORTS</c>,
/// else the host's default port.
/// </summary>
public static class HealthcheckCommand
{
    public const string Name = "healthcheck";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string[] args, int defaultPort)
    {
        ArgumentNullException.ThrowIfNull(args);

        Uri target;
        try
        {
            target = ResolveTarget(
                args,
                defaultPort,
                Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"),
                Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync($"healthcheck: {ex.Message}").ConfigureAwait(false);
            return HostRunner.Failure;
        }

        // No proxy: an HTTP(S)_PROXY in the container environment must not intercept the local probe.
        using var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = _timeout };
        try
        {
            using var response = await http.GetAsync(target).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"healthcheck: {target} -> {(int)response.StatusCode}").ConfigureAwait(false);
            return response.IsSuccessStatusCode ? HostRunner.Success : HostRunner.Failure;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"healthcheck: {target} -> {ex.Message}").ConfigureAwait(false);
            return HostRunner.Failure;
        }
    }

    internal static Uri ResolveTarget(IReadOnlyList<string> args, int defaultPort, string? httpPorts, string? urls = null)
    {
        var path = HealthEndpoints.ReadyPath;
        string? baseUrl = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "ready":
                    path = HealthEndpoints.ReadyPath;
                    break;
                case "live":
                    path = HealthEndpoints.LivePath;
                    break;
                case "--url" when i + 1 < args.Count:
                    baseUrl = args[++i];
                    break;
                default:
                    throw new ArgumentException($"unknown argument '{args[i]}'. Usage: healthcheck [ready|live] [--url <base-url>]");
            }
        }

        baseUrl ??= FromUrls(urls);
        if (baseUrl is null)
        {
            var port = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            baseUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port ?? defaultPort.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root))
        {
            throw new ArgumentException($"invalid url '{baseUrl}'");
        }

        return new Uri(root, path);
    }

    /// <summary>First <c>http://</c> binding of <c>ASPNETCORE_URLS</c>, with wildcard hosts mapped to loopback.</summary>
    private static string? FromUrls(string? urls)
    {
        var url = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (url is null)
        {
            return null;
        }

        var hostStart = "http://".Length;
        var portStart = url.LastIndexOf(':');
        var authorityEnd = url.IndexOf('/', hostStart);
        var host = portStart > hostStart && (authorityEnd < 0 || portStart < authorityEnd)
            ? url[hostStart..portStart]
            : url[hostStart..(authorityEnd < 0 ? url.Length : authorityEnd)];
        var rest = url[(hostStart + host.Length)..];
        var loopback = host is "+" or "*" or "0.0.0.0" or "[::]" or "localhost" ? "127.0.0.1" : host;
        return "http://" + loopback + rest;
    }
}
