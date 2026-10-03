using System.Net;
using Microsoft.Extensions.Options;

namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// Which reverse proxies may set <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> (<c>ReverseProxy__*</c>).
/// Loopback is always trusted (ASP.NET Core default; the Vite dev proxy). In containers the api is only
/// reachable through <c>web</c> (Caddy) on the private compose network, so deployments trust that network.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Comma-separated CIDR networks whose addresses are trusted proxies, e.g.
    /// <c>10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7</c>. Empty: loopback only.
    /// </summary>
    public string? KnownNetworks { get; set; }

    /// <summary>Comma-separated IP addresses of individual trusted proxies. Optional.</summary>
    public string? KnownProxies { get; set; }

    internal static IEnumerable<string> Split(string? list) =>
        (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        var errors = new List<string>();
        errors.AddRange(ReverseProxyOptions.Split(options.KnownNetworks)
            .Where(n => !IPNetwork.TryParse(n, out _))
            .Select(n => $"ReverseProxy:KnownNetworks: '{n}' is not a CIDR network (e.g. 172.16.0.0/12)."));
        errors.AddRange(ReverseProxyOptions.Split(options.KnownProxies)
            .Where(p => !IPAddress.TryParse(p, out _))
            .Select(p => $"ReverseProxy:KnownProxies: '{p}' is not an IP address."));
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
