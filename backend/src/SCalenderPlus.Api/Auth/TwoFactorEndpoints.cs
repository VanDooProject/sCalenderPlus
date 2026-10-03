using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// TOTP two-factor authentication (RFC 6238 authenticator apps) of the signed-in user, <c>/api/v1/me/two-factor</c>:
/// set up (new secret + <c>otpauth://</c> URI), enable (confirm with a code and the password → ten recovery codes,
/// shown once, stored hashed, single-use), disable and regenerate recovery codes (both confirmed with the password
/// or a code). Setting up and enabling need a verified email address: otherwise someone who registered a
/// stranger's address could lock its owner out behind their own authenticator. Wrong passwords and codes count
/// towards the account lockout like failed logins. Changes rotate the security stamp: other sessions end, the
/// current one is re-issued.
/// </summary>
internal static class TwoFactorEndpoints
{
    public const int RecoveryCodeCount = 10;
    private const string Issuer = "sCalenderPlus";

    public static RouteGroupBuilder MapTwoFactorEndpoints(this RouteGroupBuilder me)
    {
        var twoFactor = me.MapGroup("/two-factor");

        twoFactor.MapGet(string.Empty, GetStatusAsync).WithName("GetTwoFactorStatus")
            .WithSummary("Two-factor authentication status");
        twoFactor.MapPost("/setup", SetupAsync).WithName("SetUpTwoFactor").RequireVerifiedEmail()
            .WithSummary("Create a new authenticator secret (QR code URI); 409 while 2FA is enabled");
        twoFactor.MapPost("/enable", EnableAsync).WithName("EnableTwoFactor").RequireVerifiedEmail().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Turn on 2FA with a current authenticator code and the password; returns the recovery codes once");
        twoFactor.MapPost("/disable", DisableAsync).WithName("DisableTwoFactor").RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Turn off 2FA (confirm with password or code; no-op when off)");
        twoFactor.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync).WithName("RegenerateRecoveryCodes").RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Replace all recovery codes (confirm with password or code); returns the new codes once");

        return twoFactor;
    }

    private static async Task<Results<Ok<TwoFactorStatusResponse>, UnauthorizedHttpResult>> GetStatusAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var left = user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user).ConfigureAwait(false) : 0;
        return TypedResults.Ok(new TwoFactorStatusResponse(user.TwoFactorEnabled, left));
    }

    private static async Task<Results<Ok<TwoFactorSetupResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SetupAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            return ApiProblems.Create(ErrorCodes.Conflict, "Two-factor authentication is already enabled. Disable it first to set up a new authenticator.");
        }

        await users.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        await signIn.RefreshSignInAsync(user).ConfigureAwait(false);
        var key = (await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false))!;
        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6");
        return TypedResults.Ok(new TwoFactorSetupResponse(key, uri));
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> EnableAsync(
        EnableTwoFactorRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IAppDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            return ApiProblems.Create(ErrorCodes.Conflict, "Two-factor authentication is already enabled.");
        }

        if (await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false) is null)
        {
            return ApiProblems.Create(ErrorCodes.Conflict, "Set up the authenticator first (POST /api/v1/me/two-factor/setup).");
        }

        if (await ConfirmIdentityAsync(users, audit, db, user, request.Password, code: null, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        if (!await VerifyAuthenticatorCodeAsync(users, user, request.Code).ConfigureAwait(false))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["The code is not valid. Check the time on your device and enter the current code."],
            });
        }

        var codes = await db.InTransactionAsync(async ct =>
        {
            await users.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
            var generated = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false))!.ToList();
            audit.Record(AccountAuditActions.TwoFactorEnabled, AccountAuditActions.ResourceType, user.Id.ToString(), new { TwoFactorEnabled = false }, new { TwoFactorEnabled = true }, user.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return generated;
        }, cancellationToken).ConfigureAwait(false);

        await signIn.RefreshSignInAsync(user).ConfigureAwait(false);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DisableAsync(
        ReauthenticationRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IAppDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await ReauthenticateAsync(users, audit, db, user, request, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        if (!user.TwoFactorEnabled)
        {
            return TypedResults.NoContent();
        }

        await db.InTransactionAsync(async ct =>
        {
            await users.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);
            await users.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false); // a later setup starts with a new secret
            await users.RemoveAuthenticationTokenAsync(user, RecoveryCodes.LoginProvider, RecoveryCodes.TokenName).ConfigureAwait(false);
            audit.Record(AccountAuditActions.TwoFactorDisabled, AccountAuditActions.ResourceType, user.Id.ToString(), new { TwoFactorEnabled = true }, new { TwoFactorEnabled = false }, user.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        await signIn.RefreshSignInAsync(user).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ProblemHttpResult>> RegenerateRecoveryCodesAsync(
        ReauthenticationRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await ReauthenticateAsync(users, audit, db, user, request, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        if (!user.TwoFactorEnabled)
        {
            return ApiProblems.Create(ErrorCodes.Conflict, "Two-factor authentication is not enabled.");
        }

        var codes = await db.InTransactionAsync(async ct =>
        {
            var generated = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false))!.ToList();
            audit.Record(AccountAuditActions.RecoveryCodesRegenerated, AccountAuditActions.ResourceType, user.Id.ToString(), null, new { Count = generated.Count }, user.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return generated;
        }, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    internal static Task<bool> VerifyAuthenticatorCodeAsync(UserManager<AppUser> users, AppUser user, string? code)
    {
        var normalized = (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Length == 0
            ? Task.FromResult(false)
            : users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, normalized);
    }

    /// <summary>Null when the password or the authenticator code is correct; otherwise the problem to return.</summary>
    private static Task<ProblemHttpResult?> ReauthenticateAsync(
        UserManager<AppUser> users,
        IAuditLog audit,
        IAppDbContext db,
        AppUser user,
        ReauthenticationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Password) == string.IsNullOrEmpty(request.Code))
        {
            return Task.FromResult<ProblemHttpResult?>(ApiProblems.Create(
                ErrorCodes.ValidationFailed,
                "Send either the current password or a current authenticator code.",
                ProfileValidation.Errors("password", "Send either the current password or a current authenticator code.")));
        }

        return ConfirmIdentityAsync(users, audit, db, user, request.Password, request.Code, cancellationToken);
    }

    /// <summary>
    /// Checks the password (when given) or else the authenticator code like a login does: a locked-out account is
    /// refused, a wrong value counts towards the lockout (so a stolen session cannot guess the password or codes
    /// beyond the rate limit), a correct one resets the failure count.
    /// </summary>
    private static async Task<ProblemHttpResult?> ConfirmIdentityAsync(
        UserManager<AppUser> users,
        IAuditLog audit,
        IAppDbContext db,
        AppUser user,
        string? password,
        string? code,
        CancellationToken cancellationToken)
    {
        var failed = ApiProblems.Create(ErrorCodes.ReauthenticationFailed, "The password or code is not correct.");
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return failed;
        }

        var valid = !string.IsNullOrEmpty(password)
            ? await users.CheckPasswordAsync(user, password).ConfigureAwait(false)
            : user.TwoFactorEnabled && await VerifyAuthenticatorCodeAsync(users, user, code).ConfigureAwait(false);
        if (valid)
        {
            await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
            return null;
        }

        await users.AccessFailedAsync(user).ConfigureAwait(false);
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            audit.Record(AccountAuditActions.LockedOut, AccountAuditActions.ResourceType, user.Id.ToString(), null, new { user.LockoutEnd, Reason = "reauthentication" }, user.Id);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return failed;
    }
}

/// <summary>Where Identity keeps the recovery codes (<c>user_tokens</c>; values are hashes, see <c>AppUserStore</c>).</summary>
internal static class RecoveryCodes
{
    public const string LoginProvider = "[AspNetUserStore]";
    public const string TokenName = "RecoveryCodes";
}
