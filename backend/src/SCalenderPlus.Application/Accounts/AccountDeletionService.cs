using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Accounts;

/// <summary>Something that has to be handed over or unshared before the account can be scheduled for deletion.</summary>
/// <param name="Type"><c>group</c> or <c>calendar</c>.</param>
/// <param name="Reason"><c>sole_owner</c> (a group with other members and no other owner) or <c>shared</c> (an own calendar with grants to others).</param>
/// <param name="Count">The group's other members, or the calendar's grants.</param>
public sealed record DeletionBlocker(string Type, Guid Id, string Name, string Reason, int Count);

/// <summary>Payload of <see cref="AccountPurgeJobHandler"/>: the account and the deletion request it purges.</summary>
public sealed record AccountPurgePayload(Guid UserId, DateTimeOffset RequestedAt);

/// <summary>
/// Account deletion with a grace period (api.md "Me", permissions.md §4.6, features.md "Account deletion"):
/// <c>DELETE /me</c> marks the account (<c>users.deleted_at</c>) — its feeds answer <c>410</c> from then on, the
/// sign-in still works so the owner can cancel —, queues the confirmation email and the purge job at
/// <see cref="PurgeAt"/>. It is refused while the account is the only owner of a group with other members or owns
/// calendars it shares (<see cref="BlockersAsync"/>): those have to be handed over or unshared first, so shared
/// calendars survive. The purge itself is <see cref="AccountPurgeService"/>.
/// </summary>
public sealed class AccountDeletionService(IAppDbContext db, IJobScheduler jobs, IEmailOutbox outbox, AccountEmails emails, IAuditLog audit)
{
    /// <summary>Days between the request and the purge.</summary>
    public const int GraceDays = 14;

    public static Instant PurgeAt(Instant requestedAt) => requestedAt + Duration.FromDays(GraceDays);

    /// <summary>What keeps the account from being deleted now, by type and name.</summary>
    public async Task<IReadOnlyList<DeletionBlocker>> BlockersAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var groups = await (
            from m in db.GroupMembers.AsNoTracking()
            where m.UserId == userId && m.Role == GroupRole.Owner
            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
            let others = db.GroupMembers.Count(x => x.GroupId == g.Id && x.UserId != userId)
            let otherOwners = db.GroupMembers.Count(x => x.GroupId == g.Id && x.UserId != userId && x.Role == GroupRole.Owner)
            where others > 0 && otherOwners == 0
            select new { g.Id, g.Name, others })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var calendars = await db.Calendars.AsNoTracking()
            .Where(c => c.OwnerUserId == userId)
            .Select(c => new { c.Id, c.Name, Grants = db.CalendarGrants.Count(g => g.CalendarId == c.Id) })
            .Where(c => c.Grants > 0)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. groups.OrderBy(g => g.Name, StringComparer.CurrentCulture).Select(g => new DeletionBlocker("group", g.Id, g.Name, "sole_owner", g.others)),
            .. calendars.OrderBy(c => c.Name, StringComparer.CurrentCulture).Select(c => new DeletionBlocker("calendar", c.Id, c.Name, "shared", c.Grants)),
        ];
    }

    /// <summary><c>409 account_deletion_blocked</c> with the blockers, unless there are none.</summary>
    public async Task EnsureDeletableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var blockers = await BlockersAsync(userId, cancellationToken).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            throw new AppException(
                ErrorCodes.AccountDeletionBlocked,
                "Hand over the groups you own alone and stop sharing your calendars (or delete them) first.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["blockers"] = blockers });
        }
    }

    /// <summary>
    /// Stages what follows a deletion request (the caller has set <c>deleted_at</c> to <paramref name="requestedAt"/>
    /// and saves): the purge job, the confirmation email and the audit record.
    /// </summary>
    public void Scheduled(AccountRecord account, Instant requestedAt)
    {
        ArgumentNullException.ThrowIfNull(account);
        var purgeAt = PurgeAt(requestedAt);
        jobs.Enqueue(AccountPurgeJobHandler.JobType, new AccountPurgePayload(account.Id, requestedAt.ToDateTimeOffset()), new EnqueueOptions(RunAt: purgeAt));
        outbox.Queue(emails.DeletionScheduled(account.Email, account.DisplayName, account.Locale, account.TimeZone, purgeAt));
        audit.Record(AccountAuditActions.DeletionRequested, AccountAuditActions.ResourceType, account.Id.ToString(), null, new { RequestedAt = requestedAt.ToDateTimeOffset(), PurgeAt = purgeAt.ToDateTimeOffset() }, account.Id);
    }

    /// <summary>Stages the audit record of a cancelled request (the pending purge job finds nothing to do).</summary>
    public void Cancelled(Guid userId, Instant requestedAt) =>
        audit.Record(AccountAuditActions.DeletionCancelled, AccountAuditActions.ResourceType, userId.ToString(), new { RequestedAt = requestedAt.ToDateTimeOffset() }, null, userId);
}

/// <summary>Worker side: purges the account when its grace period is over (idempotent: a cancelled or repeated request is a no-op).</summary>
internal sealed class AccountPurgeJobHandler(AccountPurgeService purge) : JobHandler<AccountPurgePayload>
{
    public const string JobType = "account.purge";

    public override string Type => JobType;

    protected override Task HandleAsync(AccountPurgePayload payload, JobContext context, CancellationToken cancellationToken) =>
        purge.PurgeAsync(payload.UserId, Instant.FromDateTimeOffset(payload.RequestedAt), cancellationToken);
}
