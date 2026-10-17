using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// Account deletion with a 14-day grace period (api.md "Me", permissions.md §4.6): <c>GET /me/deletion</c> (state and
/// what blocks it), <c>DELETE /me</c> (confirmed with the password or an authenticator code) and
/// <c>POST /me/deletion/cancel</c>. Rules in <see cref="AccountDeletionService"/>, the purge in
/// <see cref="AccountPurgeService"/> (worker job). Requests and cancellations take the advisory lock on the user id
/// that the purge holds, so a cancellation never races a purge.
/// </summary>
internal static class AccountDeletionEndpoints
{
    public static RouteGroupBuilder MapAccountDeletionEndpoints(this RouteGroupBuilder me)
    {
        me.MapGet("/deletion", GetAsync).WithName("GetAccountDeletion")
            .WithSummary("Whether my account is scheduled for deletion, and what blocks deleting it");
        me.MapDelete(string.Empty, RequestAsync).WithName("DeleteMe")
            .RequireRateLimiting(RateLimitingSetup.Auth)
            .WithSummary("Schedule my account for deletion (14-day grace period; confirm with password or code)")
            .WithDescription("Body: { password } or { code } (403 reauthentication_failed when wrong; counts towards the lockout). Refused with 409 account_deletion_blocked (+ blockers: [{ type: group|calendar, id, name, reason: sole_owner|shared, count }]) while I am the only owner of a group with other members or own calendars shared with others: hand them over, stop sharing or delete them first. Then: my iCal feeds answer 410 at once, other sessions end, a confirmation email is sent, and after 14 days the account is purged — groups I am alone in and my personal calendars are deleted, my memberships, grants and event overrides removed (shared calendars survive), the audit trail pseudonymised, and a final email sent. Signing in stays possible until then to cancel. Already scheduled: 200 with the current state.");
        me.MapPost("/deletion/cancel", CancelAsync).WithName("CancelAccountDeletion")
            .WithSummary("Cancel the scheduled deletion of my account (idempotent)")
            .WithDescription("The feeds work again with their old URLs. Not scheduled: 200 unchanged.");
        return me;
    }

    private static async Task<Results<Ok<AccountDeletionResponse>, UnauthorizedHttpResult>> GetAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AccountDeletionService deletion,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(AccountDeletionResponse.From(user.DeletedAt, await deletion.BlockersAsync(user.Id, cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<Results<Ok<AccountDeletionResponse>, UnauthorizedHttpResult, ProblemHttpResult>> RequestAsync(
        [FromBody] ReauthenticationRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AccountDeletionService deletion,
        IAppDbContext db,
        IAuditLog audit,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.DeletedAt is not null)
        {
            return TypedResults.Ok(AccountDeletionResponse.From(user.DeletedAt, []));
        }

        // Blockers first: they need no confirmation to be shown (GET /me/deletion lists them too).
        await deletion.EnsureDeletableAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (await TwoFactorEndpoints.ReauthenticateAsync(users, audit, db, user, request, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return failure;
        }

        var requestedAt = clock.Now();
        await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(user.Id, ct).ConfigureAwait(false);
            await deletion.EnsureDeletableAsync(user.Id, ct).ConfigureAwait(false); // under the lock: a group joined meanwhile
            user.DeletedAt = requestedAt;
            var result = await users.UpdateSecurityStampAsync(user).ConfigureAwait(false); // saves; other sessions end within a minute
            if (!result.Succeeded)
            {
                throw new Application.Errors.AppException(Application.Errors.ErrorCodes.PreconditionFailed, "The account was changed meanwhile. Try again.");
            }

            deletion.Scheduled(new AccountRecord(user.Id, user.Email!, user.DisplayName, user.Locale, user.TimeZone, requestedAt), requestedAt);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        await signIn.RefreshSignInAsync(user).ConfigureAwait(false); // this session stays (to cancel), with the new stamp
        return TypedResults.Ok(AccountDeletionResponse.From(user.DeletedAt, []));
    }

    private static async Task<Results<Ok<AccountDeletionResponse>, UnauthorizedHttpResult>> CancelAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AccountDeletionService deletion,
        IAppDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal).ConfigureAwait(false);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.DeletedAt is { } requestedAt)
        {
            await db.InTransactionAsync(async ct =>
            {
                await db.LockAsync(user.Id, ct).ConfigureAwait(false);
                user.DeletedAt = null;
                var result = await users.UpdateAsync(user).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    throw new Application.Errors.AppException(Application.Errors.ErrorCodes.PreconditionFailed, "The account was changed meanwhile. Try again.");
                }

                deletion.Cancelled(user.Id, requestedAt);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(AccountDeletionResponse.From(null, await deletion.BlockersAsync(user.Id, cancellationToken).ConfigureAwait(false)));
    }
}

/// <summary>The deletion state of my account.</summary>
/// <param name="Scheduled">Set while the deletion is scheduled (the grace period runs).</param>
/// <param name="GraceDays">Days between the request and the purge (14).</param>
/// <param name="Blockers">What to hand over or stop sharing before the account can be deleted (empty while scheduled).</param>
public sealed record AccountDeletionResponse(AccountDeletionSchedule? Scheduled, int GraceDays, IReadOnlyList<AccountDeletionBlocker> Blockers)
{
    public static AccountDeletionResponse From(Instant? requestedAt, IReadOnlyList<DeletionBlocker> blockers) =>
        new(
            requestedAt is { } at ? new AccountDeletionSchedule(at.ToDateTimeOffset(), AccountDeletionService.PurgeAt(at).ToDateTimeOffset()) : null,
            AccountDeletionService.GraceDays,
            [.. blockers.Select(b => new AccountDeletionBlocker(b.Type, b.Id, b.Name, b.Reason, b.Count))]);
}

/// <param name="PurgeAt">When the account is purged (no sign-in, no cancelling after that).</param>
public sealed record AccountDeletionSchedule(DateTimeOffset RequestedAt, DateTimeOffset PurgeAt);

/// <summary>A group I am the only owner of (with other members), or a calendar of mine shared with others.</summary>
/// <param name="Type"><c>group</c> or <c>calendar</c>.</param>
/// <param name="Reason"><c>sole_owner</c> or <c>shared</c>.</param>
/// <param name="Count">Other members of the group, or grants of the calendar.</param>
public sealed record AccountDeletionBlocker(string Type, Guid Id, string Name, string Reason, int Count);
