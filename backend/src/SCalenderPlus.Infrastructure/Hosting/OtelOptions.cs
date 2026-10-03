using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>OpenTelemetry settings (<c>Otel__*</c>). Without an endpoint nothing is exported.</summary>
public sealed class OtelOptions
{
    public const string SectionName = "Otel";

    /// <summary>OTLP collector endpoint, e.g. <c>http://otel-collector:4317</c>. Optional.</summary>
    [Url]
    public string? Endpoint { get; set; }

    /// <summary>OTLP transport: <c>Grpc</c> (default, port 4317) or <c>HttpProtobuf</c> (port 4318).</summary>
    public OtlpProtocol Protocol { get; set; } = OtlpProtocol.Grpc;
}

public enum OtlpProtocol
{
    Grpc,
    HttpProtobuf,
}
