using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.OpenApi;

/// <summary>
/// Publishes the error contract (docs/architecture/api.md §2) in the document: the <c>ErrorCode</c> enum (all
/// catalogued codes), the <c>ProblemDetails</c> schema with <c>code</c>/<c>traceId</c>/<c>errors</c>, and a
/// <c>default</c> <c>application/problem+json</c> response on every <c>/api/v1</c> operation, so generated
/// clients get typed errors without each endpoint repeating it.
/// </summary>
internal static class ProblemSchemas
{
    public const string ErrorCodeSchema = "ErrorCode";
    public const string ProblemDetailsSchema = "ProblemDetails";
    public const string ProblemJson = "application/problem+json";

    public static Task TransformAsync(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        document.Components.Schemas[ErrorCodeSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable machine-readable error code (part of the contract; never renamed).",
            Enum = [.. ErrorCodes.All.Order(StringComparer.Ordinal).Select(c => (JsonNode)JsonValue.Create(c)!)],
        };
        document.Components.Schemas[ProblemDetailsSchema] = ProblemDetails(document);

        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith(ApiV1.BasePath + "/", StringComparison.Ordinal) || item.Operations is null)
            {
                continue;
            }

            foreach (var operation in item.Operations.Values)
            {
                operation.Responses ??= [];
                operation.Responses.TryAdd("default", new OpenApiResponse
                {
                    Description = "Error (RFC 9457 problem details with a stable `code`).",
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                    {
                        [ProblemJson] = new() { Schema = new OpenApiSchemaReference(ProblemDetailsSchema, document) },
                    },
                });
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema ProblemDetails(OpenApiDocument document) => new()
    {
        Type = JsonSchemaType.Object,
        Description = "RFC 9457 problem details. Further members depend on the code (e.g. `limit`, `required`, `actual`).",
        Required = new HashSet<string>(StringComparer.Ordinal) { "type", "title", "status", ProblemDetailsSetup.CodeMember },
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
            [ProblemDetailsSetup.CodeMember] = new OpenApiSchemaReference(ErrorCodeSchema, document),
            [ProblemDetailsSetup.TraceIdMember] = new OpenApiSchema { Type = JsonSchemaType.String },
            [ProblemDetailsSetup.ErrorsMember] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Validation errors by field (code `validation_failed`).",
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String },
                },
            },
        },
        AdditionalPropertiesAllowed = true,
    };
}
