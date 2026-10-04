using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory host (TestServer) for Api or Worker with explicit settings, environment "Testing" (or another one),
/// and optional extra services (test doubles, <see cref="TestPipeline"/> hooks) registered after the host's own.
/// </summary>
public sealed class HostFactory<TProgram>(
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null,
    string environment = "Testing") : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
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

        // All requests of a test host share one client address: the per-IP limits of the auth endpoints would
        // otherwise throttle tests that sign in a lot. RateLimitingTests set production-like values.
        ["RateLimiting:Auth:PermitLimit"] = "10000",
        ["RateLimiting:SignUp:PermitLimit"] = "10000",
        ["RateLimiting:InvitePreview:PermitLimit"] = "10000",

        // Worker only (validated on start); nothing listens on port 1, tests that send mail point it at Mailpit.
        ["Smtp:Host"] = "127.0.0.1",
        ["Smtp:Port"] = "1",
        ["Smtp:Security"] = "None",
        ["Smtp:From"] = "noreply@scalenderplus.test",
    };
}
