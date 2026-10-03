using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Users;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// <c>/api/v1/me</c>: the signed-in user's account and profile settings (display name, locale, IANA time zone,
/// week start). Like every mutable resource (docs/architecture/api.md §1) it has an <c>ETag</c>, and
/// <c>PATCH</c> (JSON Merge Patch) requires <c>If-Match</c>: missing → 428, stale → 412.
/// </summary>
internal static class MeEndpoints
{
    public const string Tag = "Me";
    public const string MergePatchJson = "application/merge-patch+json";

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder v1)
    {
        var me = v1.MapGroup("/me").WithTags(Tag);

        me.MapGet(string.Empty, GetMeAsync).WithName("GetMe").WithSummary("The signed-in user (with ETag)");
        me.MapPatch(string.Empty, UpdateMeAsync).WithName("UpdateMe")
            .Accepts<UpdateProfileRequest>(MergePatchJson, "application/json")
            .WithSummary("Update profile settings (JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged. Unknown time zones are 422 time_zone_invalid, unsupported locales and week starts 400 validation_failed. If-Match: the ETag of GET /me (or *); missing → 428 precondition_required, stale → 412 precondition_failed.");
        me.MapTwoFactorEndpoints();

        return me;
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMeAsync(ClaimsPrincipal principal, UserManager<AppUser> users, HttpResponse response)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var me = user.ToMe();
        response.Headers.ETag = ETagOf(me);
        return TypedResults.Ok(me);
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> UpdateMeAsync(
        [FromBody] UpdateProfileRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        IAppDbContext db,
        IAuditLog audit,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var before = user.ToMe();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return ApiProblems.Create(ErrorCodes.PreconditionRequired, "Send If-Match with the ETag of GET /api/v1/me.");
        }

        if (!ETags.Matches(ifMatch, ETagOf(before)))
        {
            return ApiProblems.Create(ErrorCodes.PreconditionFailed, "The profile was changed meanwhile. Reload it and try again.");
        }

        if (request.Locale is not null && !UserPreferences.IsSupportedLocale(request.Locale))
        {
            return ProfileValidation.LocaleInvalid("locale");
        }

        var weekStart = user.WeekStart;
        if (request.WeekStart is not null && !UserPreferences.TryParseWeekStart(request.WeekStart, out weekStart))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["weekStart"] = ["Use a lowercase day name: monday … sunday."],
            });
        }

        var displayName = request.DisplayName?.Trim();
        if (displayName is { Length: 0 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["displayName"] = ["The display name must not be empty."],
            });
        }

        if (request.TimeZone is not null && !UserPreferences.IsValidTimeZone(request.TimeZone))
        {
            return ProfileValidation.TimeZoneInvalid("timeZone", request.TimeZone);
        }

        user.DisplayName = displayName ?? user.DisplayName;
        user.Locale = request.Locale ?? user.Locale;
        user.TimeZone = request.TimeZone ?? user.TimeZone;
        user.WeekStart = weekStart;
        var after = user.ToMe();

        if (after != before)
        {
            await db.InTransactionAsync(async ct =>
            {
                var result = await users.UpdateAsync(user).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    // Only the optimistic concurrency check can fail here: a parallel update won.
                    throw new AppException(ErrorCodes.PreconditionFailed, "The profile was changed meanwhile. Reload it and try again.");
                }

                audit.Record(AccountAuditActions.ProfileUpdated, AccountAuditActions.ResourceType, user.Id.ToString(), Profile(before), Profile(after), user.Id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        response.Headers.ETag = ETagOf(after);
        return TypedResults.Ok(after);
    }

    /// <summary>Strong ETag of the representation (hash of its JSON), so it only changes when the response does.</summary>
    internal static string ETagOf(MeResponse me) => ETags.Of(me);

    private static object Profile(MeResponse me) => new { me.DisplayName, me.Locale, me.TimeZone, me.WeekStart };
}
