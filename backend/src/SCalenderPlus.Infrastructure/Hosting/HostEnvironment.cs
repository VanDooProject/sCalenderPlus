namespace SCalenderPlus.Infrastructure.Hosting;

internal static class HostEnvironment
{
    /// <summary>
    /// Environment name for CLI commands that use a plain generic host: honour <c>ASPNETCORE_ENVIRONMENT</c>
    /// like the web hosts do, so <c>migrate</c> sees the same configuration as the api.
    /// </summary>
    public static string Name =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Production";
}
