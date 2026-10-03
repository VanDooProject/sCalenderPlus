using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>A backend host started as a real process on a free localhost port; killed on dispose.</summary>
internal sealed class RunningBackend : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output;

    private RunningBackend(Process process, StringBuilder output, Uri baseUrl)
    {
        _process = process;
        _output = output;
        BaseUrl = baseUrl;
    }

    public Uri BaseUrl { get; }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public static async Task<RunningBackend> StartAsync(string project, IReadOnlyDictionary<string, string?> environment)
    {
        var baseUrl = new Uri($"http://127.0.0.1:{FreePort()}");
        var env = new Dictionary<string, string?>(environment) { ["ASPNETCORE_URLS"] = baseUrl.ToString() };
        var process = BackendProcess.Start(project, [], env, out var output);
        var backend = new RunningBackend(process, output, baseUrl);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                break;
            }

            try
            {
                using var response = await http.GetAsync(new Uri(baseUrl, "/health/live"));
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return backend;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Request timeout; retry.
            }

            await Task.Delay(200);
        }

        var log = backend.Output;
        await backend.DisposeAsync();
        throw new InvalidOperationException($"{project} did not become live. Output:\n{log}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
