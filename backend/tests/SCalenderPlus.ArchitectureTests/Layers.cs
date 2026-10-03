using System.Reflection;

namespace SCalenderPlus.ArchitectureTests;

internal static class Layers
{
    public static readonly Assembly Core = typeof(Core.Time.TimeZoneIds).Assembly;
    public static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;
    public static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Api = typeof(Api.Program).Assembly;
    public static readonly Assembly Worker = typeof(Worker.Program).Assembly;

    public const string CoreNamespace = "SCalenderPlus.Core";
    public const string ApplicationNamespace = "SCalenderPlus.Application";
    public const string InfrastructureNamespace = "SCalenderPlus.Infrastructure";
    public const string ApiNamespace = "SCalenderPlus.Api";
    public const string WorkerNamespace = "SCalenderPlus.Worker";

    // Third-party namespaces by concern.
    public const string EfCore = "Microsoft.EntityFrameworkCore";
    public const string AspNetCore = "Microsoft.AspNetCore";
    public const string Npgsql = "Npgsql";
}
