using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SCalenderPlus.ArchitectureTests;

/// <summary>
/// The single choke point of events (docs/architecture/permissions.md §8, issues #44, #50): event rows — and the
/// exceptions of series (<c>EventExceptionEntry</c>, event data too) — are read only through the permission-aware
/// <c>EventQueryService</c> and added only through its write-side counterpart <c>EventWriter</c>. Any other
/// reference to <c>DbSet&lt;Event&gt;</c> or <c>DbSet&lt;EventExceptionEntry&gt;</c> — <c>IAppDbContext.Events</c>,
/// <c>Set&lt;Event&gt;()</c>, a field or property of that type, also inside lambdas and LINQ expression trees
/// (they reference the getter via <c>ldtoken</c>) — is a violation. Scans IL with Mono.Cecil because the rule is
/// about members, not type dependencies (both services depend on <c>IAppDbContext</c> like every use case).
/// </summary>
internal static class EventAccessRules
{
    public const string EventType = "SCalenderPlus.Core.Events.Event";

    public const string ExceptionType = "SCalenderPlus.Core.Events.EventExceptionEntry";

    /// <summary>Types allowed to touch <c>DbSet&lt;Event&gt;</c> (their nested, compiler-generated types included).</summary>
    public static readonly string[] Allowed =
    [
        "SCalenderPlus.Application.Events.EventQueryService",
        "SCalenderPlus.Application.Events.EventWriter",
        "SCalenderPlus.Application.Persistence.IAppDbContext", // declares the property
        "SCalenderPlus.Infrastructure.Persistence.AppDbContext", // implements it
    ];

    /// <summary>Every place in <paramref name="assemblyPath"/> (types in <paramref name="namespacePrefix"/>) that references <c>DbSet&lt;Event&gt;</c> outside <see cref="Allowed"/>.</summary>
    public static IReadOnlyList<string> Violations(string assemblyPath, string namespacePrefix, IReadOnlyCollection<string>? allowed = null)
    {
        allowed ??= Allowed;
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        var violations = new List<string>();
        foreach (var type in module.GetTypes().Where(t => t.FullName.StartsWith(namespacePrefix, StringComparison.Ordinal)))
        {
            var outer = type;
            while (outer.DeclaringType is not null)
            {
                outer = outer.DeclaringType;
            }

            if (allowed.Contains(outer.FullName))
            {
                continue;
            }

            violations.AddRange(type.Fields.Where(f => IsEventSet(f.FieldType)).Select(f => $"{type.FullName}.{f.Name} (field)"));
            violations.AddRange(type.Properties.Where(p => IsEventSet(p.PropertyType)).Select(p => $"{type.FullName}.{p.Name} (property)"));
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference called && (IsEventSet(ReturnType(called)) || IsEventSet(called.DeclaringType)))
                    {
                        violations.Add($"{type.FullName}.{method.Name} uses {called.DeclaringType.Name}.{called.Name}");
                    }
                    else if (instruction.Operand is FieldReference field && IsEventSet(field.FieldType))
                    {
                        violations.Add($"{type.FullName}.{method.Name} uses field {field.Name}");
                    }
                    else if (instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference token && IsEventSet(token))
                    {
                        violations.Add($"{type.FullName}.{method.Name} uses typeof(DbSet<Event>)");
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>The return type with method type arguments substituted (<c>Set&lt;TEntity&gt;()</c> called as <c>Set&lt;Event&gt;()</c>).</summary>
    private static TypeReference ReturnType(MethodReference method)
    {
        if (method is not GenericInstanceMethod instance || method.ReturnType is not GenericInstanceType returned)
        {
            return method.ReturnType;
        }

        var substituted = new GenericInstanceType(returned.ElementType);
        foreach (var argument in returned.GenericArguments)
        {
            substituted.GenericArguments.Add(argument is GenericParameter { Type: GenericParameterType.Method } parameter
                ? instance.GenericArguments[parameter.Position]
                : argument);
        }

        return substituted;
    }

    private static bool IsEventSet(TypeReference type) =>
        type is GenericInstanceType generic
        && generic.ElementType.FullName == "Microsoft.EntityFrameworkCore.DbSet`1"
        && generic.GenericArguments[0].FullName is EventType or ExceptionType;
}
