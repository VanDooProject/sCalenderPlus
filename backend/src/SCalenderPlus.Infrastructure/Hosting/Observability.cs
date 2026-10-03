using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SCalenderPlus.Infrastructure.Hosting;

public static class Observability
{
    /// <summary>
    /// Logging and telemetry shared by the Api and Worker hosts:
    /// JSON console logs (one object per line, UTC timestamps) everywhere except Development, where the
    /// readable simple formatter is used; override with <c>Logging__Console__FormatterName</c>.
    /// OpenTelemetry traces and metrics (ASP.NET Core, HttpClient, Npgsql), exported via OTLP only when
    /// <c>Otel__Endpoint</c> is set.
    /// </summary>
    public static IHostApplicationBuilder AddPlatformObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureConsoleLogging(builder);
        ConfigureOpenTelemetry(builder, serviceName);

        return builder;
    }

    /// <summary>
    /// Listen on <paramref name="port"/> unless <c>ASPNETCORE_URLS</c>/<c>ASPNETCORE_HTTP_PORTS</c> (or the
    /// equivalent configuration) say otherwise. Keeps the <c>healthcheck</c> command's default in sync.
    /// </summary>
    public static IWebHostBuilder UseDefaultHttpPort(this IWebHostBuilder webHost, IConfiguration configuration, int port)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrEmpty(configuration[WebHostDefaults.ServerUrlsKey])
            && string.IsNullOrEmpty(configuration[WebHostDefaults.HttpPortsKey])
            && string.IsNullOrEmpty(configuration[WebHostDefaults.HttpsPortsKey]))
        {
            webHost.UseSetting(WebHostDefaults.HttpPortsKey, port.ToString(CultureInfo.InvariantCulture));
        }

        return webHost;
    }

    private static void ConfigureConsoleLogging(IHostApplicationBuilder builder)
    {
        var formatter = builder.Configuration["Logging:Console:FormatterName"];
        if (string.IsNullOrEmpty(formatter))
        {
            formatter = builder.Environment.IsDevelopment() ? ConsoleFormatterNames.Simple : ConsoleFormatterNames.Json;
        }

        builder.Logging.AddConsole(o => o.FormatterName = formatter);
        builder.Services.Configure<JsonConsoleFormatterOptions>(o =>
        {
            o.IncludeScopes = true;
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            o.JsonWriterOptions = new() { Indented = false };
        });
    }

    private static void ConfigureOpenTelemetry(IHostApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddOptions<OtelOptions>()
            .Bind(builder.Configuration.GetSection(OtelOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o => o.Filter = IsNotHealthProbe)
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsqlInstrumentation());

        // Wire the exporter only for a valid endpoint; an invalid one is reported by ValidateOnStart.
        var options = builder.Configuration.GetSection(OtelOptions.SectionName).Get<OtelOptions>();
        if (Uri.TryCreate(options?.Endpoint, UriKind.Absolute, out var endpoint))
        {
            var protocol = options!.Protocol == OtlpProtocol.HttpProtobuf ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
            otel.WithLogging().UseOtlpExporter(protocol, endpoint);
        }
    }

    private static bool IsNotHealthProbe(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
