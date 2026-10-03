namespace SCalenderPlus.Application.Errors;

/// <summary>
/// Stable, machine-readable error codes: the <c>code</c> member of every problem response
/// (docs/architecture/api.md §2). They are part of the public contract — the frontend maps them to messages
/// and upgrade prompts — so never rename or reuse one; add new codes instead. Each code has exactly one HTTP
/// status and title in the Api's problem catalogue (enforced by a test), and is published as the
/// <c>ErrorCode</c> enum of the OpenAPI document.
/// </summary>
public static class ErrorCodes
{
    // 400
    /// <summary>Request body/parameters failed validation; the problem carries <c>errors: { field: [messages] }</c>.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>Malformed request the server could not bind (invalid JSON, wrong parameter type, bad header).</summary>
    public const string BadRequest = "bad_request";

    // 401
    public const string Unauthenticated = "unauthenticated";
    public const string TokenExpired = "token_expired";

    // 402
    /// <summary>Plan limit reached; the problem carries <c>limit: { key, max, used }</c>.</summary>
    public const string PlanLimitReached = "plan_limit_reached";

    /// <summary>Feature not in the caller's plan; the problem carries <c>feature</c>.</summary>
    public const string FeatureNotInPlan = "feature_not_in_plan";

    // 403
    /// <summary>Caller's level is too low; the problem carries <c>required</c> and <c>actual</c> levels.</summary>
    public const string InsufficientPermission = "insufficient_permission";
    public const string TwoFactorRequired = "two_factor_required";
    public const string ExternalSharingNotAllowed = "external_sharing_not_allowed";
    public const string EmailNotVerified = "email_not_verified";

    // 404
    /// <summary>Also returned for resources the caller has level <c>none</c> on (no existence leaks).</summary>
    public const string NotFound = "not_found";

    // 405
    public const string MethodNotAllowed = "method_not_allowed";

    // 409
    public const string Conflict = "conflict";
    public const string PermissionSelfLockout = "permission_self_lockout";
    public const string CalendarFrozen = "calendar_frozen";
    public const string OverrideInvalidInTarget = "override_invalid_in_target";
    public const string UidConflict = "uid_conflict";

    // 412 / 428
    public const string PreconditionFailed = "precondition_failed";
    public const string PreconditionRequired = "precondition_required";

    // 413 / 415
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";

    // 422
    public const string RecurrenceInvalid = "recurrence_invalid";
    public const string TimeZoneInvalid = "time_zone_invalid";

    // 429
    public const string RateLimited = "rate_limited";

    // 5xx
    /// <summary>Unexpected server error; details are only logged (correlate via <c>traceId</c>).</summary>
    public const string InternalError = "internal_error";
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>All codes, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        .. typeof(ErrorCodes).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!),
    ];
}
