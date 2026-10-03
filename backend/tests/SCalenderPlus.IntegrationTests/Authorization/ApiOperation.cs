using System.Text.Json.Nodes;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>One operation of the OpenAPI document: HTTP method (upper case) and path template.</summary>
public sealed record ApiOperation(string Method, string Path)
{
    private static readonly string[] _methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public bool HasPathParameters => Path.Contains('{', StringComparison.Ordinal);

    public override string ToString() => $"{Method} {Path}";

    /// <summary>All operations of an OpenAPI 3.x document.</summary>
    public static IReadOnlyList<ApiOperation> FromDocument(JsonNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operations = new List<ApiOperation>();
        foreach (var (path, item) in document["paths"]?.AsObject() ?? [])
        {
            foreach (var method in _methods.Where(m => item?[m] is not null))
            {
                operations.Add(new ApiOperation(method.ToUpperInvariant(), path));
            }
        }

        return operations;
    }
}
