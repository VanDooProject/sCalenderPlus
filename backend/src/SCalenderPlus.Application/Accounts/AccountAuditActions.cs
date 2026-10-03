namespace SCalenderPlus.Application.Accounts;

/// <summary>
/// Audit actions of security-relevant account events (<c>audit_events.action</c>, resource type
/// <see cref="ResourceType"/>, resource id = user id). Failed logins for unknown emails are only logged
/// (no resource to attach them to, and they would let anyone flood the audit table).
/// </summary>
public static class AccountAuditActions
{
    public const string ResourceType = "user";

    public const string Registered = "user.registered";
    public const string EmailConfirmed = "user.email_confirmed";
    public const string LoginSucceeded = "user.login_succeeded";
    public const string LoginFailed = "user.login_failed";
    public const string LockedOut = "user.locked_out";
    public const string LoggedOut = "user.logged_out";
    public const string PasswordResetRequested = "user.password_reset_requested";
    public const string PasswordReset = "user.password_reset";
    public const string TwoFactorEnabled = "user.two_factor_enabled";
    public const string TwoFactorDisabled = "user.two_factor_disabled";
    public const string RecoveryCodesRegenerated = "user.recovery_codes_regenerated";
    public const string RecoveryCodeRedeemed = "user.recovery_code_redeemed";
    public const string ProfileUpdated = "user.profile_updated";
}
