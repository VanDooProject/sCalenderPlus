using Microsoft.AspNetCore.Http.HttpResults;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Problems;

/// <summary>Problem results with a catalogued <c>code</c> for endpoint handlers (status and title from the catalogue).</summary>
public static class ApiProblems
{
    public static ProblemHttpResult Create(string code, string? detail = null, IDictionary<string, object?>? extensions = null)
    {
        var entry = ProblemCatalogue.Get(code);
        var allExtensions = new Dictionary<string, object?>(extensions ?? new Dictionary<string, object?>(), StringComparer.Ordinal)
        {
            [ProblemDetailsSetup.CodeMember] = code,
        };
        return TypedResults.Problem(detail, statusCode: entry.Status, title: entry.Title, type: ProblemCatalogue.TypeFor(code).ToString(), extensions: allExtensions);
    }

    public static ProblemHttpResult From(AppException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Create(exception.Code, exception.Detail, new Dictionary<string, object?>(exception.Extensions, StringComparer.Ordinal));
    }
}
