using Microsoft.AspNetCore.Http.HttpResults;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Users;

namespace SCalenderPlus.Api.Auth;

/// <summary>Problems for invalid profile values, in the validation format (<c>errors: { field: [messages] }</c>).</summary>
internal static class ProfileValidation
{
    /// <summary><c>422 time_zone_invalid</c> (docs/architecture/api.md §2) with the field in <c>errors</c>.</summary>
    public static ProblemHttpResult TimeZoneInvalid(string field, string? value) =>
        ApiProblems.Create(
            ErrorCodes.TimeZoneInvalid,
            $"'{value}' is not a known IANA time zone id (e.g. Europe/Berlin).",
            Errors(field, "Use an IANA time zone id such as Europe/Berlin."));

    public static ValidationProblem LocaleInvalid(string field) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [$"Supported locales: {string.Join(", ", UserPreferences.SupportedLocales)}."],
        });

    public static Dictionary<string, object?> Errors(string field, string message) =>
        new(StringComparer.Ordinal)
        {
            [ProblemDetailsSetup.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] },
        };
}
