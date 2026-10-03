using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Application.Common;

/// <summary>Validation failures of use cases: <c>400 validation_failed</c> with <c>errors: { field: [message] }</c> (camelCase field paths).</summary>
public static class Validation
{
    public const string ErrorsMember = "errors";

    public static AppException Failed(string field, string message) =>
        new(ErrorCodes.ValidationFailed, message, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] },
        });
}
