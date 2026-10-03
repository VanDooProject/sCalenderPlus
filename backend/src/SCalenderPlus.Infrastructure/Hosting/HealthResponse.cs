using System.Text.Json.Serialization;

namespace SCalenderPlus.Infrastructure.Hosting;

internal sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
