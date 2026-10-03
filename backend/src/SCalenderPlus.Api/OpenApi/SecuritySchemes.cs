using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SCalenderPlus.Api.Auth;

namespace SCalenderPlus.Api.OpenApi;

/// <summary>
/// Publishes how to authenticate (docs/architecture/api.md §3): the <c>session</c> cookie scheme in the
/// components, and a security requirement on every operation that needs a signed-in user (endpoints with
/// authorization metadata that do not allow anonymous access). Personal access tokens (bearer) follow in v1.
/// </summary>
internal static class SecuritySchemes
{
    public const string Session = "session";

    public static Task AddSchemesAsync(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[Session] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = AuthenticationSetup.SessionCookieName,
            Description = "Session cookie set by POST /api/v1/auth/login (web app, same origin). Unsafe methods also need the header `X-Requested-With: scal`.",
        };
        return Task.CompletedTask;
    }

    public static Task AddRequirementAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Session, context.Document)] = [] }];
        }

        return Task.CompletedTask;
    }
}
