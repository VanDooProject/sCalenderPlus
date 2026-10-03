using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace SCalenderPlus.ArchitectureTests;

/// <summary>
/// The layering rules of docs/architecture/overview.md §2, expressed over a set of types so they can be
/// applied both to the real assemblies and to deliberately violating fixtures.
/// </summary>
internal static class ArchitectureRules
{
    /// <summary>Core is the domain: no persistence, web, or outer-layer dependencies.</summary>
    public static readonly string[] ForbiddenForCore =
    [
        Layers.EfCore, Layers.AspNetCore, Layers.Npgsql, "Microsoft.Extensions.Hosting",
        Layers.ApplicationNamespace, Layers.InfrastructureNamespace, Layers.ApiNamespace, Layers.WorkerNamespace,
    ];

    /// <summary>Application holds use cases and ports; it may use EF Core abstractions but no provider or web stack.</summary>
    public static readonly string[] ForbiddenForApplication =
    [
        Layers.AspNetCore, Layers.Npgsql,
        Layers.InfrastructureNamespace, Layers.ApiNamespace, Layers.WorkerNamespace,
    ];

    /// <summary>Infrastructure implements ports and must not know its hosts.</summary>
    public static readonly string[] ForbiddenForInfrastructure = [Layers.ApiNamespace, Layers.WorkerNamespace];

    /// <summary>
    /// Api and Worker are composition roots only: they wire things up and call Application/Infrastructure,
    /// but never touch the database directly and never depend on each other.
    /// </summary>
    public static readonly string[] ForbiddenForApi = [Layers.EfCore, Layers.Npgsql, Layers.WorkerNamespace];

    public static readonly string[] ForbiddenForWorker = [Layers.EfCore, Layers.Npgsql, Layers.ApiNamespace];

    public static TestResult MustNotDependOn(PredicateList types, string[] forbidden) =>
        types.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

    public static string Describe(TestResult result) =>
        result.IsSuccessful
            ? "ok"
            : "Violating types:\n  " + string.Join("\n  ", result.FailingTypeNames ?? []);
}
