using System.Reflection;
using NetArchTest.Rules;

namespace SCalenderPlus.ArchitectureTests;

public sealed class LayeringTests
{
    [Fact]
    public void Core_references_no_assemblies_except_the_base_library_and_NodaTime()
    {
        var unexpected = Layers.Core.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name is not ("System.Runtime" or "netstandard" or "NodaTime")
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.True(unexpected.Count == 0, "Core must depend on nothing but NodaTime. Unexpected: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void Core_has_no_persistence_web_or_outer_layer_dependencies() =>
        AssertRule(Layers.Core, ArchitectureRules.ForbiddenForCore);

    [Fact]
    public void Application_does_not_depend_on_infrastructure_hosts_or_web() =>
        AssertRule(Layers.Application, ArchitectureRules.ForbiddenForApplication);

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts() =>
        AssertRule(Layers.Infrastructure, ArchitectureRules.ForbiddenForInfrastructure);

    [Fact]
    public void Api_is_a_composition_root() =>
        AssertRule(Layers.Api, ArchitectureRules.ForbiddenForApi);

    [Fact]
    public void Worker_is_a_composition_root() =>
        AssertRule(Layers.Worker, ArchitectureRules.ForbiddenForWorker);

    [Fact]
    public void No_layer_references_the_host_assemblies()
    {
        var hosts = new[] { Layers.Api.GetName().Name, Layers.Worker.GetName().Name };
        var offenders = new[] { Layers.Core, Layers.Application, Layers.Infrastructure }
            .Where(a => a.GetReferencedAssemblies().Any(r => hosts.Contains(r.Name)))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.Empty(offenders);
    }

    private static void AssertRule(Assembly assembly, string[] forbidden)
    {
        var result = ArchitectureRules.MustNotDependOn(Types.InAssembly(assembly).That().ResideInNamespace("SCalenderPlus"), forbidden);
        Assert.True(result.IsSuccessful, ArchitectureRules.Describe(result));
    }
}
