using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Api.Auth;

/// <summary>Second login step: exactly one of <see cref="Code"/> (authenticator app) or <see cref="RecoveryCode"/>.</summary>
public sealed class LoginTwoFactorRequest
{
    /// <summary>6-digit code of the authenticator app.</summary>
    [StringLength(16)]
    public string? Code { get; init; }

    /// <summary>One of the recovery codes (each works once).</summary>
    [StringLength(32)]
    public string? RecoveryCode { get; init; }

    /// <summary>Same meaning as in the password step (persistent cookie).</summary>
    public bool RememberMe { get; init; }
}

/// <param name="Enabled">Logins need a second factor.</param>
/// <param name="RecoveryCodesLeft">Unused recovery codes (0 when disabled).</param>
public sealed record TwoFactorStatusResponse(bool Enabled, int RecoveryCodesLeft);

/// <param name="SharedKey">Base32 secret for manual entry into the authenticator app.</param>
/// <param name="AuthenticatorUri"><c>otpauth://totp/…</c> URI to show as a QR code.</param>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed class EnableTwoFactorRequest
{
    /// <summary>Current code of the authenticator app set up with <c>POST /me/two-factor/setup</c>.</summary>
    [Required]
    [StringLength(16)]
    public string Code { get; init; } = string.Empty;
}

/// <param name="RecoveryCodes">Ten single-use codes. Shown only now: the server stores hashes.</param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>Confirms a sensitive change with the current password or a current authenticator code (one of them).</summary>
public sealed class ReauthenticationRequest
{
    [StringLength(Infrastructure.Identity.IdentitySetup.MaxPasswordLength)]
    public string? Password { get; init; }

    [StringLength(16)]
    public string? Code { get; init; }
}
