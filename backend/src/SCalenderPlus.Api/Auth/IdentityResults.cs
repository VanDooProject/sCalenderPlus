using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SCalenderPlus.Core.Users;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>Mapping helpers between Identity and the API contract.</summary>
internal static class IdentityResults
{
    /// <summary>Identity tokens contain <c>+/=</c>; links carry them base64url-encoded.</summary>
    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? DecodeToken(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool IsInvalidToken(this IdentityResult result) =>
        result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken));

    /// <summary>Identity validation errors as <c>validation_failed</c>; password errors go to <paramref name="passwordField"/>.</summary>
    public static ValidationProblem ToValidationProblem(this IdentityResult result, string passwordField, string otherField = "email") =>
        TypedResults.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField : otherField)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));

    public static MeResponse ToMe(this AppUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.EmailConfirmed,
        user.DisplayName,
        user.Locale,
        user.TimeZone,
        UserPreferences.FormatWeekStart(user.WeekStart),
        user.TwoFactorEnabled,
        user.CreatedAt.ToDateTimeOffset());

    /// <summary>Audit snapshot of the account (no secrets; the redactor would catch them anyway).</summary>
    public static object ToAuditState(this AppUser user) => new
    {
        user.Email,
        EmailVerified = user.EmailConfirmed,
        user.DisplayName,
        user.Locale,
        user.TimeZone,
        WeekStart = UserPreferences.FormatWeekStart(user.WeekStart),
        user.TwoFactorEnabled,
    };
}
