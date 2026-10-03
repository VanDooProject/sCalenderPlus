using System.ComponentModel.DataAnnotations;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(AppUser.EmailMaxLength)]
    public string Email { get; init; } = string.Empty;

    /// <summary>At least 10 characters; no composition rules.</summary>
    [Required]
    [StringLength(IdentitySetup.MaxPasswordLength)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(AppUser.DisplayNameMaxLength)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary><c>en</c> (default) or <c>de</c>.</summary>
    public string? Locale { get; init; }

    /// <summary>IANA time zone id (default <c>UTC</c>); the web app sends the browser's zone.</summary>
    public string? TimeZone { get; init; }
}

public sealed class LoginRequest
{
    [Required]
    [MaxLength(AppUser.EmailMaxLength)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MaxLength(IdentitySetup.MaxPasswordLength)]
    public string Password { get; init; } = string.Empty;

    /// <summary>Persistent cookie (survives browser restarts, sliding 14 days) instead of a browser-session cookie.</summary>
    public bool RememberMe { get; init; }
}

/// <param name="TwoFactorRequired">
/// Password accepted but the account uses two-factor authentication: complete the login with
/// <c>POST /api/v1/auth/login/2fa</c> within five minutes. <paramref name="User"/> is then null.
/// </param>
/// <param name="User">The signed-in user (session cookie set) when no second factor is needed.</param>
public sealed record LoginResponse(bool TwoFactorRequired, MeResponse? User);

public sealed class ConfirmEmailRequest
{
    [Required]
    public Guid UserId { get; init; }

    /// <summary>The <c>token</c> query value of the confirmation link.</summary>
    [Required]
    [MaxLength(2048)]
    public string Token { get; init; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required]
    [MaxLength(AppUser.EmailMaxLength)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    public Guid UserId { get; init; }

    /// <summary>The <c>token</c> query value of the reset link.</summary>
    [Required]
    [MaxLength(2048)]
    public string Token { get; init; } = string.Empty;

    [Required]
    [StringLength(IdentitySetup.MaxPasswordLength)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>The signed-in user's account and profile.</summary>
/// <param name="WeekStart">First day of the week: <c>monday</c> … <c>sunday</c>.</param>
public sealed record MeResponse(
    Guid Id,
    string Email,
    bool EmailVerified,
    string DisplayName,
    string Locale,
    string TimeZone,
    string WeekStart,
    bool TwoFactorEnabled,
    DateTimeOffset CreatedAt);
