using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SCalenderPlus.Api.OpenApi;

/// <summary>
/// Members marked <c>[JsonIgnore(Condition = WhenWritingNull)]</c> are left out of responses when null (e.g. the
/// details of an event in the busy projection, api.md §4), so the schema must not list them as <c>required</c>:
/// the typed client then declares them optional.
/// </summary>
internal static class OptionalMembers
{
    public static Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        if (schema.Required is not { Count: > 0 } required)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            var ignore = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>().FirstOrDefault();
            if (ignore?.Condition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault)
            {
                required.Remove(property.Name);
            }
        }

        return Task.CompletedTask;
    }
}
