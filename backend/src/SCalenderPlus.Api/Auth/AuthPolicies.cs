using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// Authorization policies. <see cref="VerifiedEmail"/> implements the unverified-account restrictions of
/// docs/architecture/api.md §3: unverified accounts can sign in and use their own calendars, but endpoints that
/// accept email invites or pending shares, invite others, create share links or import sources call
/// <see cref="RequireVerifiedEmail{TBuilder}"/> and answer <c>403 email_not_verified</c>.
/// </summary>
public static class AuthPolicies
{
    public const string VerifiedEmail = "verified-email";

    /// <summary>Signed-in user with a confirmed email address; otherwise 401 / 403 <c>email_not_verified</c>.</summary>
    public static TBuilder RequireVerifiedEmail<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(VerifiedEmail);

    internal static AuthorizationPolicy BuildVerifiedEmailPolicy() =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new VerifiedEmailRequirement()).Build();
}

internal sealed class VerifiedEmailRequirement : IAuthorizationRequirement;

/// <summary>Reads the confirmation state from the database (not from the cookie), so it applies right after confirming.</summary>
internal sealed class VerifiedEmailHandler(UserManager<AppUser> users) : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, VerifiedEmailRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = await users.GetUserAsync(context.User).ConfigureAwait(false);
        if (user is { EmailConfirmed: true })
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Turns a failed <see cref="VerifiedEmailRequirement"/> into its problem; everything else keeps the default (challenge → 401, forbid → 403).</summary>
internal sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<VerifiedEmailRequirement>().Any() == true)
        {
            return ApiProblems.Create(ErrorCodes.EmailNotVerified, "Confirm your email address to use this feature.").ExecuteAsync(context);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
