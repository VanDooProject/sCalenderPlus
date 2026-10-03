using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace SCalenderPlus.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity for <see cref="AppUser"/> (api only): user manager with the EF Core store, token
/// providers (email confirmation and password reset via Data Protection, TOTP authenticator) and the account
/// policies of docs/architecture/api.md §3. The api adds the sign-in manager and the cookie schemes.
/// </summary>
public static class IdentitySetup
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Email confirmation and password reset links stay valid this long.</summary>
    public static readonly TimeSpan EmailTokenLifespan = TimeSpan.FromDays(1);

    /// <summary>How often a session cookie is re-checked against the user's security stamp (password reset, 2FA change …).</summary>
    public static readonly TimeSpan SecurityStampValidationInterval = TimeSpan.FromMinutes(1);

    public static IdentityBuilder AddAppIdentity(this IServiceCollection services)
    {
        var builder = services.AddIdentityCore<AppUser>(options =>
        {
            // Login is by email; the user name mirrors it, so any character valid in an email is allowed.
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = string.Empty;

            // Unverified accounts may sign in (restricted features answer 403 email_not_verified).
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedAccount = false;

            // NIST SP 800-63B: length over composition rules.
            options.Password.RequiredLength = MinPasswordLength;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 4;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
            options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

            options.Tokens.AuthenticatorIssuer = "sCalenderPlus";
        });
        builder.AddUserStore<AppUserStore>().AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = EmailTokenLifespan);
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = SecurityStampValidationInterval);
        return builder;
    }
}
