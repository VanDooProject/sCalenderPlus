using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Problems;

/// <summary>
/// Every error response is <c>application/problem+json</c> (RFC 9457) with the members of
/// docs/architecture/api.md §2: <c>type</c> (per code), <c>title</c>, <c>status</c>, <c>detail</c>,
/// <c>instance</c> (request path), <c>code</c> (stable, from <see cref="ErrorCodes"/>) and <c>traceId</c>.
/// Sources: problem results of handlers (<see cref="ApiProblems"/>), <see cref="AppException"/>s, validation
/// failures (<c>validation_failed</c> + <c>errors</c>), framework status codes (status code pages) and
/// unhandled exceptions (<c>internal_error</c>, no details).
/// </summary>
public static class ProblemDetailsSetup
{
    public const string CodeMember = "code";
    public const string TraceIdMember = "traceId";
    public const string ErrorsMember = "errors";

    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Enrich);
        services.AddExceptionHandler<AppExceptionHandler>();
        return services;
    }

    private static void Enrich(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Status = status;

        var code = problem.Extensions.TryGetValue(CodeMember, out var existing) && existing is string s
            ? s
            : problem is HttpValidationProblemDetails || problem.Extensions.ContainsKey(ErrorsMember)
                ? ErrorCodes.ValidationFailed
                : ProblemCatalogue.DefaultCodeFor(status);

        // Unknown codes are a programming error; report them as-is rather than failing the error response.
        if (ProblemCatalogue.Entries.TryGetValue(code, out var entry))
        {
            problem.Type = ProblemCatalogue.TypeFor(code).ToString();
            problem.Title = entry.Title;
        }

        problem.Instance ??= context.HttpContext.Request.Path;
        problem.Extensions[CodeMember] = code;
        problem.Extensions[TraceIdMember] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        // Never expose exception details of unexpected errors (dev included: use the logs and traceId).
        if (status >= StatusCodes.Status500InternalServerError && context.Exception is not null and not AppException)
        {
            problem.Detail = null;
            problem.Extensions.Remove("exception");
        }
    }

    /// <summary>Maps <see cref="AppException"/> and request-binding failures to their problem status.</summary>
    private sealed class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ProblemDetails problem;
            switch (exception)
            {
                case AppException app:
                    var entry = ProblemCatalogue.Get(app.Code);
                    problem = new ProblemDetails { Status = entry.Status, Detail = app.Detail };
                    foreach (var (key, value) in app.Extensions)
                    {
                        problem.Extensions[key] = value;
                    }

                    problem.Extensions[CodeMember] = app.Code;
                    break;
                case BadHttpRequestException bad:
                    problem = new ProblemDetails { Status = bad.StatusCode };
                    break;
                default:
                    return false;
            }

            httpContext.Response.StatusCode = problem.Status!.Value;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            }).ConfigureAwait(false);
        }
    }
}
