using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Users;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// <c>/api/v1/auth/…</c> (docs/architecture/api.md §3): registration, password login, logout, email
/// confirmation and password reset. Responses never reveal whether an email address has an account:
/// register and forgot-password always answer 202, failed logins are <c>401 invalid_credentials</c> whatever
/// the reason (unknown email, wrong password, locked out). Emails are queued in the same transaction as the
/// change (<see cref="IEmailOutbox"/>) together with the audit event. Rate limits per client IP: sign-up
/// (<see cref="RateLimitingSetup.SignUp"/>, plus the disposable-email blocklist) and the other credential
/// endpoints (<see cref="RateLimitingSetup.Auth"/>).
/// </summary>
internal static partial class AuthEndpoints
{
    public const string Tag = "Auth";

    /// <summary>Password hashing target for unknown emails, so their failed logins take as long as real ones.</summary>
    private static readonly AppUser _timingDummy = new() { Id = Guid.Empty, UserName = "timing@invalid" };

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder v1)
    {
        var auth = v1.MapGroup("/auth").WithTags(Tag);

        auth.MapPost("/register", RegisterAsync).WithName("Register").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.SignUp)
            .WithSummary("Create an account")
            .WithDescription("Always answers 202 for a well-formed request with an allowed email domain (disposable-email providers: 422 email_domain_not_allowed): a new account gets a confirmation email, an already registered address gets a hint email instead (no user enumeration). Sign in afterwards with POST /auth/login.");
        auth.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Sign in with email and password")
            .WithDescription("Sets the session cookie, or answers twoFactorRequired (second step: POST /auth/login/2fa). Failures are 401 invalid_credentials; after 5 failures in a row the account is locked for 15 minutes (still answered as invalid_credentials).");
        auth.MapPost("/login/2fa", LoginTwoFactorAsync).WithName("LoginTwoFactor").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Second login step: authenticator code or recovery code")
            .WithDescription("Needs the pending-login cookie from POST /auth/login (twoFactorRequired, valid 5 minutes); without it 401 unauthenticated. Wrong codes are 401 invalid_credentials and count towards the lockout; each recovery code works once.");
        auth.MapPost("/logout", LogoutAsync).WithName("Logout")
            .WithSummary("End the session (clears the session cookie)");
        auth.MapPost("/confirm-email", ConfirmEmailAsync).WithName("ConfirmEmail").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Confirm an email address with the token from the confirmation link");
        auth.MapPost("/confirm-email/resend", ResendConfirmationAsync).WithName("ResendEmailConfirmation").RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Send the confirmation email again to the signed-in user (no-op when already confirmed)");
        auth.MapPost("/forgot-password", ForgotPasswordAsync).WithName("ForgotPassword").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Request a password reset email")
            .WithDescription("Always answers 202 (no user enumeration); only registered addresses receive an email.");
        auth.MapPost("/reset-password", ResetPasswordAsync).WithName("ResetPassword").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Set a new password with the token from the reset link")
            .WithDescription("Also confirms the email address (the link proved ownership), lifts a lockout and ends all other sessions.");

        return auth;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Login failed: unknown email address.")]
    private static partial void LogUnknownEmail(ILogger logger);

    internal static ProblemHttpResult InvalidCredentials() =>
        ApiProblems.Create(ErrorCodes.InvalidCredentials, "Email address or password is incorrect.");

    internal static ProblemHttpResult TokenInvalid() =>
        ApiProblems.Create(ErrorCodes.TokenInvalid, "The link is invalid or has expired. Request a new one.");

    private static async Task<Results<Accepted, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        IEmailOutbox outbox,
        AccountEmails emails,
        EmailDomainPolicy domains,
        CancellationToken cancellationToken)
    {
        var locale = request.Locale ?? UserPreferences.DefaultLocale;
        if (!UserPreferences.IsSupportedLocale(locale))
        {
            return ProfileValidation.LocaleInvalid("locale");
        }

        var timeZone = request.TimeZone ?? UserPreferences.DefaultTimeZone;
        if (!UserPreferences.IsValidTimeZone(timeZone))
        {
            return ProfileValidation.TimeZoneInvalid("timeZone", timeZone);
        }

        var email = request.Email.Trim();
        if (domains.IsBlocked(email))
        {
            return ApiProblems.Create(
                ErrorCodes.EmailDomainNotAllowed,
                "Sign-up with disposable or blocked email providers is not possible.",
                ProfileValidation.Errors("email", "Use a permanent email address."));
        }

        return await db.InTransactionAsync<Results<Accepted, ValidationProblem, ProblemHttpResult>>(async ct =>
        {
            var existing = await users.FindByEmailAsync(email).ConfigureAwait(false);
            if (existing is not null)
            {
                _ = users.PasswordHasher.HashPassword(existing, request.Password); // same cost as a real sign-up
                outbox.Queue(emails.AlreadyRegistered(existing.Email!, existing.DisplayName, existing.Locale));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return TypedResults.Accepted((string?)null);
            }

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                UserName = email,
                DisplayName = request.DisplayName.Trim(),
                Locale = locale,
                TimeZone = timeZone,
            };
            var created = await users.CreateAsync(user, request.Password).ConfigureAwait(false);
            if (!created.Succeeded)
            {
                return created.ToValidationProblem(passwordField: "password");
            }

            var token = await users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
            outbox.Queue(emails.EmailConfirmation(email, user.DisplayName, user.Locale, user.Id, IdentityResults.EncodeToken(token)));
            audit.Record(AccountAuditActions.Registered, AccountAuditActions.ResourceType, user.Id.ToString(), null, user.ToAuditState(), user.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Accepted((string?)null);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IAppDbContext db,
        IAuditLog audit,
        ILogger<AuthLog> logger,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            _ = users.PasswordHasher.HashPassword(_timingDummy, request.Password);
            LogUnknownEmail(logger);
            return InvalidCredentials();
        }

        return await db.InTransactionAsync<Results<Ok<LoginResponse>, ProblemHttpResult>>(async ct =>
        {
            var wasLockedOut = await users.IsLockedOutAsync(user).ConfigureAwait(false);
            var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true).ConfigureAwait(false);
            if (wasLockedOut)
            {
                // Identity refuses a locked-out account before verifying the password; hash anyway, or the fast
                // answer would tell that the address has an account (lock it with five guesses, then time it).
                _ = users.PasswordHasher.HashPassword(_timingDummy, request.Password);
            }

            var userId = user.Id.ToString();

            if (result.Succeeded)
            {
                audit.Record(AccountAuditActions.LoginSucceeded, AccountAuditActions.ResourceType, userId, null, new { Method = "password" }, user.Id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return TypedResults.Ok(new LoginResponse(TwoFactorRequired: false, user.ToMe()));
            }

            if (result.RequiresTwoFactor)
            {
                return TypedResults.Ok(new LoginResponse(TwoFactorRequired: true, User: null));
            }

            if (result.IsLockedOut && !wasLockedOut)
            {
                audit.Record(AccountAuditActions.LockedOut, AccountAuditActions.ResourceType, userId, null, new { LockoutEnd = user.LockoutEnd }, user.Id);
            }
            else
            {
                audit.Record(AccountAuditActions.LoginFailed, AccountAuditActions.ResourceType, userId, null, new { Reason = result.IsLockedOut ? "locked_out" : "invalid_password" }, user.Id);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return InvalidCredentials();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<LoginResponse>, ValidationProblem, ProblemHttpResult>> LoginTwoFactorAsync(
        LoginTwoFactorRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IAppDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) == string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["Send either an authenticator code or a recovery code."],
            });
        }

        var user = await signIn.GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return ApiProblems.Create(ErrorCodes.Unauthenticated, "No pending login: sign in with email and password first.");
        }

        return await db.InTransactionAsync<Results<Ok<LoginResponse>, ValidationProblem, ProblemHttpResult>>(async ct =>
        {
            var wasLockedOut = await users.IsLockedOutAsync(user).ConfigureAwait(false);
            var byRecoveryCode = string.IsNullOrWhiteSpace(request.Code);
            var result = byRecoveryCode
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode!).ConfigureAwait(false)
                : await signIn.TwoFactorAuthenticatorSignInAsync(
                    request.Code!.Replace(" ", string.Empty, StringComparison.Ordinal), request.RememberMe, rememberClient: false).ConfigureAwait(false);
            var userId = user.Id.ToString();

            if (result.Succeeded)
            {
                if (byRecoveryCode)
                {
                    var left = await users.CountRecoveryCodesAsync(user).ConfigureAwait(false);
                    audit.Record(AccountAuditActions.RecoveryCodeRedeemed, AccountAuditActions.ResourceType, userId, null, new { RecoveryCodesLeft = left }, user.Id);
                }

                audit.Record(AccountAuditActions.LoginSucceeded, AccountAuditActions.ResourceType, userId, null, new { Method = byRecoveryCode ? "recovery_code" : "totp" }, user.Id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return TypedResults.Ok(new LoginResponse(TwoFactorRequired: false, user.ToMe()));
            }

            if (result.IsLockedOut && !wasLockedOut)
            {
                audit.Record(AccountAuditActions.LockedOut, AccountAuditActions.ResourceType, userId, null, new { LockoutEnd = user.LockoutEnd }, user.Id);
            }
            else
            {
                audit.Record(AccountAuditActions.LoginFailed, AccountAuditActions.ResourceType, userId, null, new { Reason = result.IsLockedOut ? "locked_out" : "invalid_second_factor" }, user.Id);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ApiProblems.Create(ErrorCodes.InvalidCredentials, "The code is not correct.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NoContent> LogoutAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IAppDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        await signIn.SignOutAsync().ConfigureAwait(false);
        if (users.GetUserId(principal) is { } userId)
        {
            audit.Record(AccountAuditActions.LoggedOut, AccountAuditActions.ResourceType, userId, null, null, Guid.Parse(userId));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        GroupInviteService invites,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString()).ConfigureAwait(false);
        var token = IdentityResults.DecodeToken(request.Token);
        if (user is null || token is null)
        {
            return TokenInvalid();
        }

        return await db.InTransactionAsync<Results<NoContent, ProblemHttpResult>>(async ct =>
        {
            var wasConfirmed = user.EmailConfirmed;
            var result = await users.ConfirmEmailAsync(user, token).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return TokenInvalid();
            }

            if (!wasConfirmed)
            {
                audit.Record(AccountAuditActions.EmailConfirmed, AccountAuditActions.ResourceType, user.Id.ToString(), new { EmailVerified = false }, new { EmailVerified = true }, user.Id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await invites.JoinPendingEmailInvitesAsync(user.Id, user.Email!, ct).ConfigureAwait(false);
            }

            return TypedResults.NoContent();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> ResendConfirmationAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        IAppDbContext db,
        IEmailOutbox outbox,
        AccountEmails emails,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.EmailConfirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
            outbox.Queue(emails.EmailConfirmation(user.Email!, user.DisplayName, user.Locale, user.Id, IdentityResults.EncodeToken(token)));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Accepted> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        IEmailOutbox outbox,
        AccountEmails emails,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim()).ConfigureAwait(false);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
            outbox.Queue(emails.PasswordReset(user.Email!, user.DisplayName, user.Locale, user.Id, IdentityResults.EncodeToken(token)));
            audit.Record(AccountAuditActions.PasswordResetRequested, AccountAuditActions.ResourceType, user.Id.ToString(), null, null, user.Id);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetPasswordAsync(
        ResetPasswordRequest request,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        GroupInviteService invites,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString()).ConfigureAwait(false);
        var token = IdentityResults.DecodeToken(request.Token);
        if (user is null || token is null)
        {
            return TokenInvalid();
        }

        return await db.InTransactionAsync<Results<NoContent, ValidationProblem, ProblemHttpResult>>(async ct =>
        {
            var result = await users.ResetPasswordAsync(user, token, request.NewPassword).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result.IsInvalidToken() ? TokenInvalid() : result.ToValidationProblem(passwordField: "newPassword");
            }

            // The link proved control of the mailbox; a lockout from someone guessing the old password ends.
            var wasConfirmed = user.EmailConfirmed;
            user.EmailConfirmed = true;
            await users.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
            await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
            audit.Record(AccountAuditActions.PasswordReset, AccountAuditActions.ResourceType, user.Id.ToString(), null, null, user.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (!wasConfirmed)
            {
                await invites.JoinPendingEmailInvitesAsync(user.Id, user.Email!, ct).ConfigureAwait(false);
            }

            return TypedResults.NoContent();
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Log category of the auth endpoints.</summary>
internal sealed class AuthLog;
