using System.Net;
using System.Net.Http.Json;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// The authorization matrix (workflow.md §6): every operation of the OpenAPI document is either listed in
/// <see cref="AnonymousOperations"/> with a reason, or has <see cref="Cases"/> — at least
/// "anonymous → 401", plus "cross-tenant → 404" when the path has parameters, plus one case per relevant
/// actor/level. <see cref="AuthorizationMatrixCoverageTests"/> fails for any operation without an entry, and
/// <see cref="AuthorizationMatrixTests"/> executes every case against the real api and PostgreSQL.
/// See README.md in this folder for how to add an endpoint.
/// </summary>
public static class AuthorizationMatrix
{
    public static IReadOnlyList<AnonymousOperation> AnonymousOperations { get; } =
    [
        Anonymous("GET", "/health/live", "Container liveness probe; status only."),
        Anonymous("GET", "/health/ready", "Container readiness probe; check names and statuses only."),

        // Auth (#28): the ways to get or recover a session. Answers never reveal whether an account exists.
        Anonymous("POST", "/api/v1/auth/register", "Sign-up.", _ => JsonContent.Create(new
        {
            email = $"new-{Guid.NewGuid():N}@matrix.example.test",
            password = MatrixScenario.Password,
            displayName = "New",
        })),
        Anonymous("POST", "/api/v1/auth/login", "Password login.", s => JsonContent.Create(new { email = s.Get("email:user"), password = MatrixScenario.Password })),
        Anonymous("POST", "/api/v1/auth/forgot-password", "Reset request by email (always 202).", s => JsonContent.Create(new { email = s.Get("email:user") })),
        Anonymous(
            "POST",
            "/api/v1/auth/confirm-email",
            "Opened from the emailed link in any browser; the token is the credential.",
            _ => JsonContent.Create(new { userId = Guid.CreateVersion7(), token = "invalid" }),
            HttpStatusCode.BadRequest),
        Anonymous(
            "POST",
            "/api/v1/auth/reset-password",
            "Opened from the emailed link; the token is the credential.",
            _ => JsonContent.Create(new { userId = Guid.CreateVersion7(), token = "invalid", newPassword = "new password 123" }),
            HttpStatusCode.BadRequest),
    ];

    public static IReadOnlyList<MatrixCase> Cases { get; } =
    [
        // Auth (#28)
        .. For("POST", "/api/v1/auth/logout")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.FreshSession, HttpStatusCode.NoContent),
        .. For("POST", "/api/v1/auth/confirm-email/resend")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.NoContent)
            .Expect(Actors.User, HttpStatusCode.NoContent),

        // Me (#28)
        .. For("GET", "/api/v1/me")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.OK)
            .Expect(Actors.OtherUser, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized), // password alone is no session

        // Profile settings (#33)
        .. For("PATCH", "/api/v1/me")
            .WithBody(_ => JsonContent.Create(new { weekStart = "sunday" }))
            .WithHeaders(new Dictionary<string, string>(StringComparer.Ordinal) { ["If-Match"] = "*" })
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),

        // Two-factor authentication (#31)
        .. For("POST", "/api/v1/auth/login/2fa")
            .WithBody(s => JsonContent.Create(new { code = s.CurrentTotp(Actors.TwoFactorPending.Name) }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherUser, HttpStatusCode.Unauthorized) // a session is not a pending login
            .Expect(Actors.TwoFactorPending, HttpStatusCode.OK),
        .. For("GET", "/api/v1/me/two-factor")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorUser, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),
        .. For("POST", "/api/v1/me/two-factor/setup")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Forbidden, "email_not_verified")
            .Expect(Actors.OtherUser, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorUser, HttpStatusCode.Conflict)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),
        .. For("POST", "/api/v1/me/two-factor/enable")
            .WithBody(s => JsonContent.Create(new { code = s.CurrentTotp(Actors.User.Name), password = MatrixScenario.Password }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Forbidden, "email_not_verified")
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.TwoFactorUser, HttpStatusCode.Conflict)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),
        .. For("POST", "/api/v1/me/two-factor/disable")
            .WithBody(_ => JsonContent.Create(new { password = MatrixScenario.Password }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherUser, HttpStatusCode.NoContent) // 2FA off: confirmed no-op
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),
        .. For("POST", "/api/v1/me/two-factor/recovery-codes")
            .WithBody(_ => JsonContent.Create(new { password = MatrixScenario.Password }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.TwoFactorUser, HttpStatusCode.OK)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Conflict)
            .Expect(Actors.TwoFactorPending, HttpStatusCode.Unauthorized),

        // Groups (#34): members see the group, admins change it, owners delete it; non-members get 404.
        .. For("GET", "/api/v1/groups")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.OtherTenant, HttpStatusCode.OK),
        .. For("POST", "/api/v1/groups")
            .WithBody(_ => JsonContent.Create(new { name = "New group" }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.Created)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Created),
        .. For("GET", "/api/v1/groups/{id}")
            .WithRoute(Lions)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.GroupMember, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("PATCH", "/api/v1/groups/{id}")
            .WithRoute(Lions)
            .WithBody(_ => JsonContent.Create(new { name = "lions" }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/groups/{id}")
            .WithRoute(Lions)
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Forbidden)
            .WithRoute(s => Route(s, "group:doomed"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),

        // Group membership (#35): owners manage everyone, admins members/viewers, members and viewers nobody.
        .. For("GET", "/api/v1/groups/{id}/members")
            .WithRoute(Lions)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.GroupMember, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("PATCH", "/api/v1/groups/{id}/members/{userId}")
            .WithRoute(s => Member(s, "lions-role-target"))
            .WithBody(_ => JsonContent.Create(new { role = "member" }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/groups/{id}/members/{userId}")
            .WithRoute(s => Member(s, "lions-kept"))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .WithRoute(s => Member(s, "lions-removed-by-admin"))
            .Expect(Actors.GroupAdmin, HttpStatusCode.NoContent)
            .WithRoute(s => Member(s, "lions-removed-by-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),
        .. For("POST", "/api/v1/groups/{id}/transfer")
            .WithRoute(Lions)
            .WithBody(s => JsonContent.Create(new { userId = s.Get("user:" + Actors.GroupOwner.Name) })) // to oneself: no-op
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),

        // Group invites (#36): admins and owners invite (verified email required), verified users accept.
        .. For("POST", "/api/v1/groups/{id}/invites")
            .WithRoute(Lions)
            .WithBody(_ => JsonContent.Create(new { role = "member" }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Forbidden, "email_not_verified")
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Created)
            .Expect(Actors.GroupOwner, HttpStatusCode.Created),
        .. For("GET", "/api/v1/groups/{id}/invites")
            .WithRoute(Lions)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/invites/{id}")
            .WithRoute(s => Route(s, "invite:revocable"))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.NoContent) // idempotent: either may run first
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),
        .. For("POST", "/api/v1/invites/accept")
            .WithBody(s => JsonContent.Create(new { token = s.Get("token:acceptable") }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Forbidden, "email_not_verified")
            .Expect(Actors.User, HttpStatusCode.OK) // joins "lions" as member; no other case depends on it
            .Expect(Actors.GroupMember, HttpStatusCode.OK), // already a member: keeps the role

        // Calendars (#41): levels from the permission engine — none → 404, too low → 403.
        .. For("GET", "/api/v1/calendars")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.OK)
            .Expect(Actors.OtherTenant, HttpStatusCode.OK),
        .. For("POST", "/api/v1/calendars")
            .WithBody(_ => JsonContent.Create(new { name = "Personal", defaultTimeZone = "Europe/Berlin" }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.Created)
            .Expect(Actors.UnverifiedUser, HttpStatusCode.Created)
            .WithBody(s => JsonContent.Create(new { name = "Fixtures", defaultTimeZone = "Europe/Berlin", groupId = s.Get("group:lions") }))
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarEditor, HttpStatusCode.NotFound) // a grant on a group calendar is no membership
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Created)
            .Expect(Actors.GroupOwner, HttpStatusCode.Created),
        .. For("GET", "/api/v1/calendars/{id}")
            .WithRoute(LionsCalendar)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.OK)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.GroupMember, HttpStatusCode.OK)
            .Expect(Actors.CalendarEditor, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("PATCH", "/api/v1/calendars/{id}")
            .WithRoute(LionsCalendar)
            .WithBody(_ => JsonContent.Create(new { name = "lions" }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/calendars/{id}")
            .WithRoute(LionsCalendar)
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Forbidden) // manage is not owner
            .WithRoute(s => Route(s, "calendar:lions-doomed"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),

        // Calendar grants (#42): managers share the calendar.
        .. For("GET", "/api/v1/calendars/{id}/grants")
            .WithRoute(LionsCalendar)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("POST", "/api/v1/calendars/{id}/grants")
            .WithRoute(LionsCalendar)
            .WithBody(s => GrantTo(s, "grant-target-admin"))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Created)
            .WithBody(s => GrantTo(s, "grant-target-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.Created),
        .. For("PATCH", "/api/v1/calendars/{id}/grants/{grantId}")
            .WithRoute(s => Grant(s, Actors.CalendarEditor.Name))
            .WithBody(_ => JsonContent.Create(new { level = "edit" })) // unchanged: no effect on other cases
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/calendars/{id}/grants/{grantId}")
            .WithRoute(s => Grant(s, "grant-removed-by-admin"))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.NoContent)
            .WithRoute(s => Grant(s, "grant-removed-by-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),

        // Events (#44, #45): calendar level contribute creates; event levels from the engine — none → 404, too low → 403.
        // The window lists what each caller sees (none-level events are left out, not refused).
        .. For("GET", "/api/v1/events")
            .WithQuery("from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z")
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.User, HttpStatusCode.OK)
            .Expect(Actors.OtherTenant, HttpStatusCode.OK)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.OK)
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("POST", "/api/v1/events")
            .WithBody(s => JsonContent.Create(new
            {
                calendarId = s.Get("calendar:lions"),
                title = "Matrix",
                start = new { dateTime = "2026-11-03T18:00:00" },
                end = new { dateTime = "2026-11-03T19:00:00" },
            }))
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Created)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Created)
            .Expect(Actors.GroupAdmin, HttpStatusCode.Created)
            .Expect(Actors.GroupOwner, HttpStatusCode.Created),
        .. For("GET", "/api/v1/events/{id}")
            .WithRoute(LionsEvent)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.OK) // busy projection
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .Expect(Actors.GroupMember, HttpStatusCode.OK)
            .Expect(Actors.CalendarEditor, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("PATCH", "/api/v1/events/{id}")
            .WithRoute(LionsEvent)
            .WithBody(_ => JsonContent.Create(new { title = "lions" })) // unchanged: no effect on other cases
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden) // not the creator: read
            .Expect(Actors.CalendarEditor, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/events/{id}")
            .WithRoute(LionsEvent)
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .WithRoute(s => Route(s, "event:lions-deleted-by-editor"))
            .Expect(Actors.CalendarEditor, HttpStatusCode.NoContent)
            .WithRoute(s => Route(s, "event:lions-deleted-by-admin"))
            .Expect(Actors.GroupAdmin, HttpStatusCode.NoContent)
            .WithRoute(s => Route(s, "event:lions-deleted-by-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),

        // Overrides (#46): only floor holders (event level manage) read and replace them; others who see the event 403.
        .. For("GET", "/api/v1/events/{id}/overrides")
            .WithRoute(LionsEvent)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden) // not the creator: read
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden) // edit is not a floor
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("PUT", "/api/v1/events/{id}/overrides")
            .WithRoute(s => Route(s, "event:lions-overrides"))
            .WithBody(_ => JsonContent.Create(new { overrides = new[] { new { principal = new { type = "everyone" }, level = "free_busy" } } }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .WithBody(_ => JsonContent.Create(new { overrides = new[] { new { principal = new { type = "everyone" }, level = "manage" } } }))
            .Expect(Actors.GroupOwner, HttpStatusCode.UnprocessableEntity, "override_invalid") // manage only through floors
            .WithRoute(s => Route(s, "event:lions-by-member"))
            .WithBody(s => JsonContent.Create(new { overrides = new[] { new { principal = new { type = "user", id = s.Get("user:" + Actors.CalendarFreeBusy.Name) }, level = "read" } } }))
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden, "external_sharing_not_allowed"), // creator floor, but the free_busy user is outside the audience

        // Explain (#47): one's own level for everyone who sees the event; other users' only for calendar managers.
        .. For("GET", "/api/v1/events/{id}/access/explain")
            .WithRoute(LionsEvent)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.OK) // own level
            .Expect(Actors.GroupViewer, HttpStatusCode.OK)
            .WithQuery(s => "userId=" + s.Get("user:" + Actors.GroupViewer.Name))
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden) // contribute: not a calendar manager
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),

        // Move (#48): manage on the event and contribute on the target.
        .. For("POST", "/api/v1/events/{id}/move")
            .WithRoute(LionsEvent)
            .WithBody(s => JsonContent.Create(new { targetCalendarId = s.Get("calendar:lions-target") }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.Forbidden) // edit, no floor
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden) // not the creator
            .WithRoute(s => Route(s, "event:lions-moved-by-admin"))
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .WithRoute(s => Route(s, "event:lions-moved-by-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),

        // Occurrence edits (#51): edit on the series (exceptions have no ACL of their own); member reads only.
        .. For("PATCH", "/api/v1/events/{id}/occurrences/{recurrenceId}")
            .WithRoute(s => Occurrence(s, "event:lions-series", "2026-11-09T17:00:00Z"))
            .WithBody(_ => JsonContent.Create(new { title = "lions-series" })) // the series' title: no exception, no effect on other cases
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .Expect(Actors.CalendarEditor, HttpStatusCode.OK)
            .Expect(Actors.GroupAdmin, HttpStatusCode.OK)
            .Expect(Actors.GroupOwner, HttpStatusCode.OK),
        .. For("DELETE", "/api/v1/events/{id}/occurrences/{recurrenceId}")
            .WithRoute(s => Occurrence(s, "event:lions-series", "2026-11-09T17:00:00Z"))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .WithRoute(s => Occurrence(s, "event:lions-series", "2026-11-16T17:00:00Z"))
            .Expect(Actors.CalendarEditor, HttpStatusCode.NoContent)
            .WithRoute(s => Occurrence(s, "event:lions-series", "2026-11-23T17:00:00Z"))
            .Expect(Actors.GroupAdmin, HttpStatusCode.NoContent)
            .WithRoute(s => Occurrence(s, "event:lions-series", "2026-11-30T17:00:00Z"))
            .Expect(Actors.GroupOwner, HttpStatusCode.NoContent),
        .. For("POST", "/api/v1/events/{id}/split")
            .WithRoute(s => Route(s, "event:lions-series"))
            .WithBody(_ => JsonContent.Create(new { recurrenceId = "2026-12-07T17:00:00Z", title = "Later" }))
            .WithHeaders(IfMatchAny)
            .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
            .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
            .Expect(Actors.NonMember, HttpStatusCode.NotFound)
            .Expect(Actors.CalendarFreeBusy, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupViewer, HttpStatusCode.Forbidden)
            .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
            .WithRoute(s => Route(s, "event:lions-split-by-editor"))
            .Expect(Actors.CalendarEditor, HttpStatusCode.Created) // edit suffices: the new series keeps the admin as creator
            .WithRoute(s => Route(s, "event:lions-split-by-admin"))
            .Expect(Actors.GroupAdmin, HttpStatusCode.Created)
            .WithRoute(s => Route(s, "event:lions-split-by-owner"))
            .Expect(Actors.GroupOwner, HttpStatusCode.Created),
    ];

    // A property, not a field: Cases is initialized first (static initializers run in declaration order).
    private static IReadOnlyDictionary<string, string> IfMatchAny => new Dictionary<string, string>(StringComparer.Ordinal) { ["If-Match"] = "*" };

    private static IReadOnlyDictionary<string, string> Lions(MatrixScenario s) => Route(s, "group:lions");

    private static IReadOnlyDictionary<string, string> LionsCalendar(MatrixScenario s) => Route(s, "calendar:lions");

    private static IReadOnlyDictionary<string, string> LionsEvent(MatrixScenario s) => Route(s, "event:lions");

    private static Dictionary<string, string> Occurrence(MatrixScenario s, string series, string recurrenceId) =>
        new(StringComparer.Ordinal) { ["id"] = s.Get(series), ["recurrenceId"] = recurrenceId };

    private static Dictionary<string, string> Grant(MatrixScenario s, string holder) =>
        new(StringComparer.Ordinal) { ["id"] = s.Get("calendar:lions"), ["grantId"] = s.Get($"grant:lions:{holder}") };

    private static JsonContent GrantTo(MatrixScenario s, string user) =>
        JsonContent.Create(new { principal = new { type = "user", id = s.Get("user:" + user) }, level = "read" });

    private static Dictionary<string, string> Member(MatrixScenario s, string user) =>
        new(StringComparer.Ordinal) { ["id"] = s.Get("group:lions"), ["userId"] = s.Get("user:" + user) };

    private static Dictionary<string, string> Route(MatrixScenario s, string resource, string name = "id") =>
        new(StringComparer.Ordinal) { [name] = s.Get(resource) };

    private static AnonymousOperation Anonymous(
        string method,
        string path,
        string reason,
        Func<MatrixScenario, HttpContent?>? body = null,
        HttpStatusCode? expected = null) =>
        new(new ApiOperation(method, path), reason, body, expected);

    /// <summary>Starts the cases of one operation: <c>.. For("GET", "/api/v1/groups/{id}").Expect(...)</c>.</summary>
    internal static OperationCases For(string method, string path) => new(new ApiOperation(method, path));
}

/// <summary>Fluent builder for the cases of one operation; enumerate it into <see cref="AuthorizationMatrix.Cases"/>.</summary>
internal sealed class OperationCases(ApiOperation operation) : IEnumerable<MatrixCase>
{
    private readonly List<MatrixCase> _cases = [];
    private Func<MatrixScenario, IReadOnlyDictionary<string, string>>? _routeValues;
    private Func<MatrixScenario, HttpContent?>? _body;
    private IReadOnlyDictionary<string, string>? _headers;
    private Func<MatrixScenario, string>? _query;

    /// <summary>Route values for every following case, e.g. <c>s => new() { ["id"] = s.Get("group:lions") }</c>.</summary>
    public OperationCases WithRoute(Func<MatrixScenario, IReadOnlyDictionary<string, string>> routeValues)
    {
        _routeValues = routeValues;
        return this;
    }

    /// <summary>A valid request body for every following case (authorization must be checked before validation).</summary>
    public OperationCases WithBody(Func<MatrixScenario, HttpContent?> body)
    {
        _body = body;
        return this;
    }

    /// <summary>Extra request headers for every following case, e.g. <c>If-Match: *</c> for conditional updates.</summary>
    public OperationCases WithHeaders(IReadOnlyDictionary<string, string> headers)
    {
        _headers = headers;
        return this;
    }

    /// <summary>A query string (without <c>?</c>) for every following case, e.g. the required window of <c>GET /events</c>.</summary>
    public OperationCases WithQuery(string query) => WithQuery(_ => query);

    /// <summary>A query string built from the scenario (e.g. a seeded user id) for every following case.</summary>
    public OperationCases WithQuery(Func<MatrixScenario, string> query)
    {
        _query = query;
        return this;
    }

    /// <param name="code">The problem code of an error status when it is not the default of <see cref="MatrixCase.ExpectedProblemCode"/>.</param>
    public OperationCases Expect(MatrixActor actor, HttpStatusCode expected, string? code = null)
    {
        _cases.Add(new MatrixCase(operation, actor, expected, _routeValues, _body, _headers, code, _query));
        return this;
    }

    public IEnumerator<MatrixCase> GetEnumerator() => _cases.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
