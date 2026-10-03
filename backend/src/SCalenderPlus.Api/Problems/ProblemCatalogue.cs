using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Problems;

/// <summary>
/// HTTP status and (English) title of every <see cref="ErrorCodes">error code</see>, plus the default code for
/// errors produced by the framework (routing 404/405, binding 400, unhandled exceptions 500, …).
/// Titles become localizable via <c>Accept-Language</c> later; <c>code</c> never changes.
/// </summary>
public static class ProblemCatalogue
{
    /// <summary>Base of the problem <c>type</c> URIs: <c>{TypeBaseUri}{code-with-dashes}</c>.</summary>
    public const string TypeBaseUri = "https://scalenderplus.app/problems/";

    public static IReadOnlyDictionary<string, ProblemEntry> Entries { get; } = new Dictionary<string, ProblemEntry>(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = new(StatusCodes.Status400BadRequest, "One or more validation errors occurred"),
        [ErrorCodes.BadRequest] = new(StatusCodes.Status400BadRequest, "Bad request"),
        [ErrorCodes.TokenInvalid] = new(StatusCodes.Status400BadRequest, "Invalid or expired link"),
        [ErrorCodes.Unauthenticated] = new(StatusCodes.Status401Unauthorized, "Authentication required"),
        [ErrorCodes.TokenExpired] = new(StatusCodes.Status401Unauthorized, "Token expired"),
        [ErrorCodes.InvalidCredentials] = new(StatusCodes.Status401Unauthorized, "Invalid credentials"),
        [ErrorCodes.PlanLimitReached] = new(StatusCodes.Status402PaymentRequired, "Plan limit reached"),
        [ErrorCodes.FeatureNotInPlan] = new(StatusCodes.Status402PaymentRequired, "Feature not in plan"),
        [ErrorCodes.InsufficientPermission] = new(StatusCodes.Status403Forbidden, "Insufficient permission"),
        [ErrorCodes.TwoFactorRequired] = new(StatusCodes.Status403Forbidden, "Two-factor authentication required"),
        [ErrorCodes.ExternalSharingNotAllowed] = new(StatusCodes.Status403Forbidden, "External sharing not allowed"),
        [ErrorCodes.EmailNotVerified] = new(StatusCodes.Status403Forbidden, "Email address not verified"),
        [ErrorCodes.NotFound] = new(StatusCodes.Status404NotFound, "Not found"),
        [ErrorCodes.MethodNotAllowed] = new(StatusCodes.Status405MethodNotAllowed, "Method not allowed"),
        [ErrorCodes.Conflict] = new(StatusCodes.Status409Conflict, "Conflict"),
        [ErrorCodes.PermissionSelfLockout] = new(StatusCodes.Status409Conflict, "Change would lock you out"),
        [ErrorCodes.CalendarFrozen] = new(StatusCodes.Status409Conflict, "Calendar is frozen"),
        [ErrorCodes.OverrideInvalidInTarget] = new(StatusCodes.Status409Conflict, "Override invalid in target calendar"),
        [ErrorCodes.UidConflict] = new(StatusCodes.Status409Conflict, "Event UID already exists"),
        [ErrorCodes.PreconditionFailed] = new(StatusCodes.Status412PreconditionFailed, "Precondition failed"),
        [ErrorCodes.PreconditionRequired] = new(StatusCodes.Status428PreconditionRequired, "Precondition required"),
        [ErrorCodes.PayloadTooLarge] = new(StatusCodes.Status413PayloadTooLarge, "Payload too large"),
        [ErrorCodes.UnsupportedMediaType] = new(StatusCodes.Status415UnsupportedMediaType, "Unsupported media type"),
        [ErrorCodes.RecurrenceInvalid] = new(StatusCodes.Status422UnprocessableEntity, "Invalid recurrence"),
        [ErrorCodes.TimeZoneInvalid] = new(StatusCodes.Status422UnprocessableEntity, "Invalid time zone"),
        [ErrorCodes.RateLimited] = new(StatusCodes.Status429TooManyRequests, "Too many requests"),
        [ErrorCodes.InternalError] = new(StatusCodes.Status500InternalServerError, "Internal server error"),
        [ErrorCodes.ServiceUnavailable] = new(StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
    };

    /// <summary>Code for a problem produced without one (framework errors, bare status results).</summary>
    public static string DefaultCodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.InsufficientPermission,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status412PreconditionFailed => ErrorCodes.PreconditionFailed,
        StatusCodes.Status413PayloadTooLarge => ErrorCodes.PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        StatusCodes.Status428PreconditionRequired => ErrorCodes.PreconditionRequired,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
        >= 500 => ErrorCodes.InternalError,
        _ => ErrorCodes.BadRequest,
    };

    public static Uri TypeFor(string code) => new(TypeBaseUri + code.Replace('_', '-'));

    public static ProblemEntry Get(string code) =>
        Entries.TryGetValue(code, out var entry)
            ? entry
            : throw new ArgumentException($"Unknown error code '{code}': add it to ErrorCodes and the ProblemCatalogue.", nameof(code));
}

public sealed record ProblemEntry(int Status, string Title);
