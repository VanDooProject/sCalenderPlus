using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>In-memory host (TestServer) for Api or Worker with explicit settings, environment "Testing".</summary>
public sealed class HostFactory<TProgram>(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}

internal static class TestSettings
{
    /// <summary>Nothing listens on port 1: connecting fails fast.</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=scal;Username=scal;Password=scal;Timeout=2";

    public static Dictionary<string, string?> For(string connectionString, bool autoMigrate = false) => new()
    {
        ["App:PublicBaseUrl"] = "https://app.example.test",
        ["ConnectionStrings:Default"] = connectionString,
        ["Database:AutoMigrate"] = autoMigrate ? "true" : "false",
    };
}
