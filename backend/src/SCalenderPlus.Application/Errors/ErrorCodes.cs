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

    /// <summary>An email confirmation, password reset or group invite token is invalid, expired, revoked or used up.</summary>
    public const string TokenInvalid = "token_invalid";

    // 401
    public const string Unauthenticated = "unauthenticated";
    public const string TokenExpired = "token_expired";

    /// <summary>Login failed: unknown email, wrong password, wrong second factor or locked-out account (deliberately indistinguishable).</summary>
    public const string InvalidCredentials = "invalid_credentials";

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

    /// <summary>An unsafe request (POST/PUT/PATCH/DELETE) to the api lacks <c>X-Requested-With: scal</c> (CSRF protection).</summary>
    public const string CsrfHeaderMissing = "csrf_header_missing";

    /// <summary>A sensitive account change (disable 2FA, new recovery codes) needs the current password or an authenticator code, and the one given is wrong.</summary>
    public const string ReauthenticationFailed = "reauthentication_failed";

    /// <summary>An email invite is used by an account whose verified email is a different address.</summary>
    public const string InviteEmailMismatch = "invite_email_mismatch";

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

    /// <summary>The change would leave the group without an owner (last owner leaving, demoted or removed).</summary>
    public const string LastOwner = "last_owner";

    /// <summary>The billing owner cannot leave, be demoted or removed before billing is transferred to another owner.</summary>
    public const string BillingOwnerTransferRequired = "billing_owner_transfer_required";

    /// <summary>Billing can only be transferred to a member with role owner.</summary>
    public const string BillingOwnerMustBeOwner = "billing_owner_must_be_owner";

    /// <summary>The group is over its plan limit (frozen): no invites or role changes.</summary>
    public const string GroupFrozen = "group_frozen";

    /// <summary>The group still owns calendars: transfer or delete them before deleting the group.</summary>
    public const string GroupHasCalendars = "group_has_calendars";

    // 412 / 428
    public const string PreconditionFailed = "precondition_failed";
    public const string PreconditionRequired = "precondition_required";

    // 413 / 415
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";

    // 422

    /// <summary>A malformed recurrence (RRULE syntax, UNTIL before the start, bad RDATE/EXDATE); carries <c>errors</c> with the field.</summary>
    public const string RecurrenceInvalid = "recurrence_invalid";
    public const string TimeZoneInvalid = "time_zone_invalid";

    /// <summary>
    /// Valid RFC 5545 recurrence outside the supported subset (sub-daily FREQ, BYHOUR/BYMINUTE/BYSECOND, BYYEARDAY,
    /// BYWEEKNO, RSCALE/SKIP, extension parts); carries <c>errors["recurrence.rrule"]</c> naming the part.
    /// </summary>
    public const string RecurrenceNotSupported = "recurrence_not_supported";

    /// <summary>
    /// A proposed set of event overrides has invalid entries (level above <c>edit</c>, the same principal twice, a
    /// group or user that cannot be selected); carries <c>violations</c> and <c>errors.overrides</c>.
    /// </summary>
    public const string OverrideInvalid = "override_invalid";

    /// <summary>Sign-up with an email domain that is blocked (disposable-email providers, operator blocklist); carries <c>errors.email</c>.</summary>
    public const string EmailDomainNotAllowed = "email_domain_not_allowed";

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
