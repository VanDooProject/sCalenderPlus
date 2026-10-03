using System.Globalization;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

/// <summary>
/// Group invitation emails (en, de) and invite links. Links point to the web app route <see cref="InvitePath"/>
/// below <c>App:PublicBaseUrl</c> (<c>?token=…</c>); the app posts the token to <c>POST /api/v1/invites/accept</c>
/// after sign-in (and sign-up plus email verification for new users).
/// </summary>
public sealed class GroupEmails(IOptions<AppOptions> options)
{
    public const string InvitePath = "/invite";

    public Uri InviteLink(string token)
    {
        var baseUrl = new Uri(options.Value.PublicBaseUrl.TrimEnd('/') + "/");
        return new Uri(new Uri(baseUrl, InvitePath.TrimStart('/')), "?token=" + Uri.EscapeDataString(token));
    }

    public EmailMessage Invitation(string to, string locale, string inviterName, string groupName, GroupRole role, string token, Instant expiresAt)
    {
        var url = InviteLink(token);
        var german = string.Equals(locale, "de", StringComparison.Ordinal);
        var expires = expiresAt.ToDateTimeOffset().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var template = german
            ? new EmailTemplate(
                $"Einladung in die Gruppe „{groupName}“",
                [
                    "Hallo,",
                    $"{inviterName} lädt dich als {RoleName(role, german)} in die Gruppe „{groupName}“ bei sCalenderPlus ein.",
                    $"Die Einladung gilt bis {expires} und nur für diese E-Mail-Adresse: Melde dich mit ihr an oder registriere dich und bestätige sie – danach bist du automatisch Mitglied.",
                    "Wenn du die Einladung nicht erwartet hast, ignoriere diese E-Mail.",
                ],
                new EmailAction("Einladung annehmen", url))
            : new EmailTemplate(
                $"Invitation to the group “{groupName}”",
                [
                    "Hi,",
                    $"{inviterName} invites you to join the group “{groupName}” on sCalenderPlus as {RoleName(role, german)}.",
                    $"The invitation is valid until {expires} and only for this email address: sign in with it, or sign up and confirm it — you join automatically afterwards.",
                    "If you did not expect this invitation, ignore this email.",
                ],
                new EmailAction("Accept invitation", url));
        return template.Render(to);
    }

    private static string RoleName(GroupRole role, bool german) => (role, german) switch
    {
        (GroupRole.Owner, true) => "Eigentümer:in",
        (GroupRole.Admin, true) => "Admin",
        (GroupRole.Member, true) => "Mitglied",
        (GroupRole.Viewer, true) => "Leser:in",
        (GroupRole.Owner, false) => "an owner",
        (GroupRole.Admin, false) => "an admin",
        (GroupRole.Member, false) => "a member",
        _ => "a viewer",
    };
}
