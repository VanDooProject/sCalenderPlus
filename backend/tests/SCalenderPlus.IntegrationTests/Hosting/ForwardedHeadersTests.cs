using Microsoft.AspNetCore.Http;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Hosting;

/// <summary>X-Forwarded-For/-Proto are honoured from trusted proxies only (loopback + ReverseProxy:KnownNetworks).</summary>
public sealed class ForwardedHeadersTests
{
    private static readonly Dictionary<string, RequestDelegate> _echo = new(StringComparer.Ordinal)
    {
        ["/echo"] = context => context.Response.WriteAsync($"{context.Request.Scheme} {context.Connection.RemoteIpAddress}"),
    };

    [Theory]
    [InlineData("127.0.0.1", null, "https 203.0.113.7")] // loopback is always trusted (dev proxy)
    [InlineData("172.18.0.5", "10.0.0.0/8,172.16.0.0/12", "https 203.0.113.7")] // compose network
    [InlineData("172.18.0.5", null, "http 172.18.0.5")] // not configured: ignored
    [InlineData("198.51.100.9", "10.0.0.0/8,172.16.0.0/12", "http 198.51.100.9")] // public peer: spoofing ignored
    public async Task Forwarded_headers_apply_only_from_known_networks(string peer, string? knownNetworks, string expected)
    {
        var settings = TestSettings.For(TestSettings.UnreachableDatabase);
        settings["ReverseProxy:KnownNetworks"] = knownNetworks;
        await using var factory = new HostFactory<ApiProgram>(settings, services => TestPipeline.Add(services, _echo));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/__test/echo", UriKind.Relative));
        request.Headers.Add(TestPipeline.RemoteIpHeader, peer);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Client_address_is_the_first_untrusted_hop_of_a_proxy_chain()
    {
        // Traefik (172.18.0.2) → Caddy (172.18.0.3) → api: Caddy appends Traefik's address to the client's.
        var settings = TestSettings.For(TestSettings.UnreachableDatabase);
        settings["ReverseProxy:KnownNetworks"] = "172.16.0.0/12";
        await using var factory = new HostFactory<ApiProgram>(settings, services => TestPipeline.Add(services, _echo));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/__test/echo", UriKind.Relative));
        request.Headers.Add(TestPipeline.RemoteIpHeader, "172.18.0.3");
        request.Headers.Add("X-Forwarded-For", "198.51.100.1, 203.0.113.7, 172.18.0.2");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("https 203.0.113.7", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_known_network_fails_startup()
    {
        var environment = new Dictionary<string, string?>
        {
            ["App__PublicBaseUrl"] = "https://app.example.test",
            ["ConnectionStrings__Default"] = TestSettings.UnreachableDatabase,
            ["ReverseProxy__KnownNetworks"] = "10.0.0.0/8,not-a-network",
        };

        var result = await BackendProcess.RunAsync(BackendProcess.Api, [], environment, TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not-a-network", result.Output, StringComparison.Ordinal);
    }
}
