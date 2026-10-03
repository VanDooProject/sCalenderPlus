using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary><c>/api/v1/me</c>: the signed-in user's account and profile.</summary>
internal static class MeEndpoints
{
    public const string Tag = "Me";

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder v1)
    {
        var me = v1.MapGroup("/me").WithTags(Tag);

        me.MapGet(string.Empty, GetMeAsync).WithName("GetMe").WithSummary("The signed-in user");

        return me;
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMeAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(user.ToMe());
    }
}
