using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;

namespace SCalenderPlus.Application.Accounts;

/// <summary>
/// Transactional emails of the account flows (verify email, reset password, sign-up of a taken address) in the
/// user's locale (en, de). Links point to routes of the web app below <c>App:PublicBaseUrl</c>
/// (<see cref="VerifyEmailPath"/>, <see cref="ResetPasswordPath"/>); the app posts the query values to the api.
/// </summary>
public sealed class AccountEmails(IOptions<AppOptions> options)
{
    /// <summary>Web app route that confirms an email address: <c>?userId=…&amp;token=…</c> → <c>POST /api/v1/auth/confirm-email</c>.</summary>
    public const string VerifyEmailPath = "/verify-email";

    /// <summary>Web app route of the new-password form: <c>?userId=…&amp;token=…</c> → <c>POST /api/v1/auth/reset-password</c>.</summary>
    public const string ResetPasswordPath = "/reset-password";

    public const string LoginPath = "/login";
    public const string ForgotPasswordPath = "/forgot-password";

    public EmailMessage EmailConfirmation(string to, string displayName, string locale, Guid userId, string token)
    {
        var url = Link(VerifyEmailPath, userId, token);
        var template = IsGerman(locale)
            ? new EmailTemplate(
                "Bestätige deine E-Mail-Adresse",
                [$"Hallo {displayName},", "bitte bestätige deine E-Mail-Adresse für sCalenderPlus. Erst danach kannst du andere einladen und Kalender teilen.", "Wenn du kein Konto angelegt hast, ignoriere diese E-Mail."],
                new EmailAction("E-Mail-Adresse bestätigen", url))
            : new EmailTemplate(
                "Confirm your email address",
                [$"Hi {displayName},", "please confirm your email address for sCalenderPlus. You need a confirmed address to invite others and share calendars.", "If you did not create an account, ignore this email."],
                new EmailAction("Confirm email address", url));
        return template.Render(to, displayName);
    }

    public EmailMessage PasswordReset(string to, string displayName, string locale, Guid userId, string token)
    {
        var url = Link(ResetPasswordPath, userId, token);
        var template = IsGerman(locale)
            ? new EmailTemplate(
                "Passwort zurücksetzen",
                [$"Hallo {displayName},", "jemand (hoffentlich du) möchte das Passwort deines sCalenderPlus-Kontos zurücksetzen. Der Link ist einen Tag gültig.", "Wenn du das nicht warst, ignoriere diese E-Mail; dein Passwort bleibt unverändert."],
                new EmailAction("Neues Passwort festlegen", url))
            : new EmailTemplate(
                "Reset your password",
                [$"Hi {displayName},", "someone (hopefully you) asked to reset the password of your sCalenderPlus account. The link is valid for one day.", "If this wasn't you, ignore this email; your password stays unchanged."],
                new EmailAction("Choose a new password", url));
        return template.Render(to, displayName);
    }

    /// <summary>Sent instead of a second account when someone signs up with a registered address (no user enumeration).</summary>
    public EmailMessage AlreadyRegistered(string to, string displayName, string locale)
    {
        var url = new Uri(BaseUrl, ForgotPasswordPath);
        var template = IsGerman(locale)
            ? new EmailTemplate(
                "Du hast bereits ein Konto",
                [$"Hallo {displayName},", "jemand hat versucht, mit dieser E-Mail-Adresse ein neues sCalenderPlus-Konto anzulegen. Du hast bereits eines – melde dich einfach an.", "Passwort vergessen? Über den Link kannst du ein neues festlegen. Wenn du das nicht warst, ignoriere diese E-Mail."],
                new EmailAction("Passwort zurücksetzen", url))
            : new EmailTemplate(
                "You already have an account",
                [$"Hi {displayName},", "someone tried to create a new sCalenderPlus account with this email address. You already have one, so just sign in.", "Forgot your password? Use the link to choose a new one. If this wasn't you, ignore this email."],
                new EmailAction("Reset password", url));
        return template.Render(to, displayName);
    }

    private Uri BaseUrl => new(options.Value.PublicBaseUrl.TrimEnd('/') + "/");

    private Uri Link(string path, Guid userId, string token) =>
        new(new Uri(BaseUrl, path.TrimStart('/')), $"?userId={userId}&token={Uri.EscapeDataString(token)}");

    private static bool IsGerman(string? locale) => string.Equals(locale, "de", StringComparison.Ordinal);
}
