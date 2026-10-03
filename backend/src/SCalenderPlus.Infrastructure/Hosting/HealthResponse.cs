using System.Text.Json.Serialization;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>Body of <c>/health/live</c> and <c>/health/ready</c>: overall status and each check's status, nothing else.</summary>
public sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
