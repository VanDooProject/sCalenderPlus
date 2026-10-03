using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// Cookie sessions for the web app (docs/architecture/api.md §3), issued by ASP.NET Core Identity:
/// <list type="bullet">
/// <item><c>__Host-scal</c> session cookie: HttpOnly, Secure (always, the api sees https through trusted
/// forwarded headers), SameSite=Lax, path <c>/</c>, no domain; sliding expiry of <see cref="SessionLifetime"/>,
/// re-validated against the security stamp every minute (password reset or 2FA change ends other sessions).</item>
/// <item><c>__Host-scal-2fa</c>: the short-lived "password verified, second factor pending" cookie of a 2FA login.</item>
/// <item>The api never redirects: unauthenticated → 401 <c>unauthenticated</c>, forbidden → 403 problem.</item>
/// </list>
/// </summary>
internal static class AuthenticationSetup
{
    public const string SessionCookieName = "__Host-scal";
    public const string TwoFactorCookieName = "__Host-scal-2fa";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan TwoFactorLoginLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddAppIdentity().AddSignInManager();

        services.ConfigureApplicationCookie(options =>
        {
            ConfigureCookie(options, SessionCookieName);
            options.ExpireTimeSpan = SessionLifetime;
            options.SlidingExpiration = true;
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            ConfigureCookie(options, TwoFactorCookieName);
            options.ExpireTimeSpan = TwoFactorLoginLifetime;
            options.SlidingExpiration = false;
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, o => ConfigureCookie(o, "__Host-scal-2fa-device"));
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, o => ConfigureCookie(o, "__Host-scal-external"));

        services.AddAuthorization(options => options.AddPolicy(AuthPolicies.VerifiedEmail, AuthPolicies.BuildVerifiedEmailPolicy()));
        services.AddScoped<IAuthorizationHandler, VerifiedEmailHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, string name)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null; // __Host- cookies must not have a Domain attribute

        // An API answers with status codes, never with redirects to login/access-denied pages.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
