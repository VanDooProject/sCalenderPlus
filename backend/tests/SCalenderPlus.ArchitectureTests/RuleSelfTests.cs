using NetArchTest.Rules;

namespace SCalenderPlus.ArchitectureTests;

/// <summary>Guards against rules that silently pass: a violating reference must make a rule fail.</summary>
public sealed class RuleSelfTests
{
    private const string FixturesNamespace = "SCalenderPlus.ArchitectureTests.Fixtures";

    [Fact]
    public void Core_rule_fails_for_a_type_depending_on_AspNetCore()
    {
        var result = ArchitectureRules.MustNotDependOn(Fixtures("CoreViolation"), ArchitectureRules.ForbiddenForCore);

        Assert.False(result.IsSuccessful);
        Assert.Contains($"{FixturesNamespace}.CoreViolation.DomainTypeUsingHttp", result.FailingTypeNames);
    }

    [Fact]
    public void Composition_root_rule_fails_for_an_Api_type_depending_on_the_Worker()
    {
        var result = ArchitectureRules.MustNotDependOn(Fixtures("ApiViolation"), ArchitectureRules.ForbiddenForApi);

        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void Core_rule_passes_for_a_clean_type()
    {
        var result = ArchitectureRules.MustNotDependOn(Fixtures("Clean"), ArchitectureRules.ForbiddenForCore);

        Assert.True(result.IsSuccessful, ArchitectureRules.Describe(result));
    }

    private static PredicateList Fixtures(string name) =>
        Types.InAssembly(typeof(RuleSelfTests).Assembly).That().ResideInNamespace($"{FixturesNamespace}.{name}");
}
