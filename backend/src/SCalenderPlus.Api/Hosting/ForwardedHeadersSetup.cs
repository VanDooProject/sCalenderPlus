using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace SCalenderPlus.Api.Hosting;

/// <summary>
/// Honours <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from trusted proxies only, so the api sees the
/// client's scheme (needed for <c>Secure</c>/<c>__Host-</c> cookies) and address (per-IP rate limits, audit).
/// The chain is Traefik → web (Caddy) → api; both proxies are on private networks, so the forward limit is
/// unbounded and processing stops at the first untrusted address.
/// </summary>
internal static class ForwardedHeadersSetup
{
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((options, proxy) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = null;

                // Invalid entries are reported by the validator on start; skip them here.
                foreach (var network in ReverseProxyOptions.Split(proxy.Value.KnownNetworks))
                {
                    if (System.Net.IPNetwork.TryParse(network, out var parsed))
                    {
                        options.KnownIPNetworks.Add(parsed);
                    }
                }

                foreach (var address in ReverseProxyOptions.Split(proxy.Value.KnownProxies))
                {
                    if (IPAddress.TryParse(address, out var parsed))
                    {
                        options.KnownProxies.Add(parsed);
                    }
                }
            });

        return services;
    }
}
