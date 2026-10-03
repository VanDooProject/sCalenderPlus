using System.Security.Claims;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Auth;

internal static class PrincipalExtensions
{
    /// <summary>The signed-in user's id (Identity's <see cref="ClaimTypes.NameIdentifier"/>); 401 without one.</summary>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new AppException(ErrorCodes.Unauthenticated, "Sign in first.");
}
