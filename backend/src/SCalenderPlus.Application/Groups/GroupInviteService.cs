using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

/// <param name="Email">Invite by email (single use) when set; otherwise an invite link.</param>
/// <param name="ExpiresInDays">1–30; default 14 for email invites, 7 for links.</param>
/// <param name="MaxUses">Links only: 1–1000, default 50. Email invites are single-use.</param>
public sealed record NewInvite(string? Email, GroupRole Role, int? ExpiresInDays = null, int? MaxUses = null);

/// <param name="Link">The invite link with the token (links only, shown once); email invites are only sent by email.</param>
public sealed record CreatedInvite(GroupInvite Invite, Uri? Link);

/// <summary>
/// Group invites by email and by link (issue #36). Tokens are 32 random bytes (base64url), stored only as
/// SHA-256 hashes. Email invites bind to the <b>verified</b> address: accepting needs a confirmed account with
/// that email, and confirming an email address joins every pending invite for it
/// (<see cref="JoinPendingEmailInvitesAsync"/>). Responses never reveal whether an address has an account.
/// </summary>
public sealed class GroupInviteService(
    IAppDbContext db,
    IAuditLog audit,
    IUserDirectory users,
    IEmailOutbox outbox,
    GroupEmails emails,
    IGroupEntitlements entitlements,
    IEnumerable<IGroupMembershipObserver> observers,
    GroupService groups,
    IClock clock)
{
    public async Task<CreatedInvite> CreateAsync(Guid actorId, Guid groupId, NewInvite request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (group, actor) = await LoadAsync(actorId, groupId, cancellationToken).ConfigureAwait(false);
        var email = request.Email?.Trim();
        var isLink = string.IsNullOrEmpty(email);

        var decision = MembershipPolicy.CheckInvite(actor.Role, request.Role, isLink, group.FrozenAt is not null);
        if (!decision.IsAllowed)
        {
            throw GroupErrors.From(decision, actor.Role);
        }

        if (!isLink && request.MaxUses is not null and not 1)
        {
            throw Validation.Failed("maxUses", "Email invites are single-use; leave maxUses out.");
        }

        if (request.MaxUses is < 1 or > GroupInvite.MaxLinkUses)
        {
            throw Validation.Failed("maxUses", $"Use 1 to {GroupInvite.MaxLinkUses} uses.");
        }

        if (request.ExpiresInDays is < 1 or > GroupInvite.MaxExpiryDays)
        {
            throw Validation.Failed("expiresInDays", $"Invites expire after 1 to {GroupInvite.MaxExpiryDays} days.");
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = clock.Now();
        var invite = new GroupInvite
        {
            Id = Guid.CreateVersion7(),
            GroupId = groupId,
            Email = isLink ? null : email,
            NormalizedEmail = isLink ? null : users.NormalizeEmail(email!),
            TokenHash = HashToken(token),
            Role = request.Role,
            MaxUses = isLink ? request.MaxUses ?? GroupInvite.DefaultLinkUses : 1,
            ExpiresAt = now + Duration.FromDays(request.ExpiresInDays ?? (isLink ? GroupInvite.DefaultLinkExpiryDays : GroupInvite.DefaultEmailExpiryDays)),
            CreatedBy = actorId,
            CreatedAt = now,
        };

        if (!isLink)
        {
            // One valid invite per address: a new one replaces pending ones (e.g. resent with another role).
            var previous = await db.GroupInvites
                .Where(i => i.GroupId == groupId && i.NormalizedEmail == invite.NormalizedEmail && i.RevokedAt == null && i.Uses < i.MaxUses && i.ExpiresAt > now)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var old in previous)
            {
                old.RevokedAt = now;
                audit.Record(GroupAuditActions.InviteRevoked, GroupAuditActions.ResourceType, groupId.ToString(), InviteState(old), new { RevokedAt = now.ToDateTimeOffset(), ReplacedBy = invite.Id }, group.OwnerUserId);
            }

            var inviter = (await users.GetAsync([actorId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(actorId);
            var recipient = await users.FindByEmailAsync(email!, cancellationToken).ConfigureAwait(false);
            outbox.Queue(emails.Invitation(email!, recipient?.Locale ?? inviter?.Locale ?? "en", inviter?.DisplayName ?? "Someone", group.Name, invite.Role, token, invite.ExpiresAt));
        }

        db.GroupInvites.Add(invite);
        audit.Record(GroupAuditActions.InviteCreated, GroupAuditActions.ResourceType, groupId.ToString(), null, InviteState(invite), group.OwnerUserId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CreatedInvite(invite, isLink ? emails.InviteLink(token) : null);
    }

    /// <summary>Pending (usable) invites of the group, ordered by id; admins and owners only.</summary>
    public async Task<Page<GroupInvite>> ListPendingAsync(Guid actorId, Guid groupId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var (_, actor) = await LoadAsync(actorId, groupId, cancellationToken).ConfigureAwait(false);
        if (!MembershipPolicy.CanManageInvites(actor.Role))
        {
            throw GroupErrors.InsufficientRole(GroupRole.Admin, actor.Role);
        }

        var now = clock.Now();
        var invites = db.GroupInvites.AsNoTracking()
            .Where(i => i.GroupId == groupId && i.RevokedAt == null && i.Uses < i.MaxUses && i.ExpiresAt > now);
        if (page.After is { } after)
        {
            invites = invites.Where(i => i.Id > after);
        }

        var rows = await invites.OrderBy(i => i.Id).Take(page.Limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return page.ToPage(rows, i => i.Id);
    }

    /// <summary>Revokes an invite (idempotent: revoking an expired, used or revoked invite is a no-op).</summary>
    public async Task RevokeAsync(Guid actorId, Guid inviteId, CancellationToken cancellationToken = default)
    {
        var invite = await db.GroupInvites.SingleOrDefaultAsync(i => i.Id == inviteId, cancellationToken).ConfigureAwait(false)
            ?? throw GroupErrors.InviteNotFound();
        var actor = await db.GroupMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == invite.GroupId && m.UserId == actorId, cancellationToken).ConfigureAwait(false)
            ?? throw GroupErrors.InviteNotFound();

        var decision = MembershipPolicy.CheckInviteRevocation(actor.Role, invite.Role);
        if (!decision.IsAllowed)
        {
            throw GroupErrors.From(decision, actor.Role);
        }

        var now = clock.Now();
        if (!invite.IsPending(now))
        {
            return;
        }

        var billingOwner = await db.Groups.Where(g => g.Id == invite.GroupId).Select(g => g.OwnerUserId).SingleAsync(cancellationToken).ConfigureAwait(false);
        invite.RevokedAt = now;
        audit.Record(GroupAuditActions.InviteRevoked, GroupAuditActions.ResourceType, invite.GroupId.ToString(), InviteState(invite), new { RevokedAt = now.ToDateTimeOffset() }, billingOwner);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Joins the group of the invite. The caller's email must be verified (the endpoint's policy); email invites
    /// additionally need the same address. Members accepting again keep their role (an email invite is used up,
    /// a link use is not counted).
    /// </summary>
    /// <exception cref="AppException"><c>token_invalid</c> (unknown, expired, revoked, used up), <c>invite_email_mismatch</c>.</exception>
    public async Task<GroupView> AcceptAsync(Guid actorId, string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        var hash = HashToken(token.Trim());
        var now = clock.Now();
        var invite = await db.GroupInvites.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        if (invite is null || !invite.IsPending(now))
        {
            throw GroupErrors.InviteInvalid();
        }

        var actor = (await users.GetAsync([actorId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(actorId);
        if (actor is not { EmailVerified: true })
        {
            throw new AppException(ErrorCodes.EmailNotVerified, "Confirm your email address to accept invitations.");
        }

        if (!invite.IsLink && invite.NormalizedEmail != users.NormalizeEmail(actor.Email))
        {
            throw new AppException(ErrorCodes.InviteEmailMismatch, "This invitation was sent to another email address. Sign in with that address to accept it.");
        }

        await db.InTransactionAsync(
            async ct =>
            {
                await JoinAsync(invite, actorId, auto: false, now, ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        return await groups.GetAsync(actorId, invite.GroupId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Joins every pending email invite for <paramref name="verifiedEmail"/> — call it in the transaction that
    /// confirms the user's email address (confirmation link, password reset). Invites the user can't use any more
    /// (group over its member limit) stay pending.
    /// </summary>
    /// <returns>The groups joined.</returns>
    public async Task<IReadOnlyList<Guid>> JoinPendingEmailInvitesAsync(Guid userId, string verifiedEmail, CancellationToken cancellationToken = default)
    {
        var normalized = users.NormalizeEmail(verifiedEmail);
        var now = clock.Now();
        var invites = await db.GroupInvites.AsNoTracking()
            .Where(i => i.NormalizedEmail == normalized && i.RevokedAt == null && i.Uses < i.MaxUses && i.ExpiresAt > now)
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var joined = new List<Guid>();
        foreach (var invite in invites)
        {
            try
            {
                if (await JoinAsync(invite, userId, auto: true, now, cancellationToken).ConfigureAwait(false))
                {
                    joined.Add(invite.GroupId);
                }
            }
            catch (AppException e) when (e.Code is ErrorCodes.PlanLimitReached or ErrorCodes.GroupFrozen or ErrorCodes.TokenInvalid)
            {
                // Refused before anything was written (group full or frozen, invite used up meanwhile): confirming
                // the address must still succeed; the invite of a full or frozen group stays pending.
            }
        }

        return joined;
    }

    /// <returns>True when the user became a member (false: already one).</returns>
    private async Task<bool> JoinAsync(GroupInvite invite, Guid userId, bool auto, Instant now, CancellationToken cancellationToken)
    {
        var group = await db.Groups.AsNoTracking().SingleAsync(g => g.Id == invite.GroupId, cancellationToken).ConfigureAwait(false);
        var isMember = await db.GroupMembers.AnyAsync(m => m.GroupId == invite.GroupId && m.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (isMember && invite.IsLink)
        {
            return false;
        }

        if (!isMember)
        {
            // Frozen groups take no new members (MembershipPolicy), also through invites created before the freeze.
            if (group.FrozenAt is not null)
            {
                throw GroupErrors.Frozen();
            }

            await entitlements.EnsureCanAddMemberAsync(group.Id, group.OwnerUserId, cancellationToken).ConfigureAwait(false);
        }

        // Atomic use: parallel accepts of the last use cannot both succeed.
        var used = await db.GroupInvites
            .Where(i => i.Id == invite.Id && i.RevokedAt == null && i.Uses < i.MaxUses && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Uses, i => i.Uses + 1), cancellationToken).ConfigureAwait(false);
        if (used == 0)
        {
            throw GroupErrors.InviteInvalid();
        }

        if (isMember)
        {
            return false;
        }

        // Touch the group row like every membership change, so a concurrent role change or removal that read the
        // old state fails with 412. Unconditional (no xmin check): parallel joins don't invalidate each other, and
        // the invite row lock above already serializes the joiners of one invite. Gone meanwhile → invalid invite.
        var touched = await db.Groups.Where(g => g.Id == group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.UpdatedAt, now), cancellationToken).ConfigureAwait(false);
        if (touched == 0)
        {
            throw GroupErrors.InviteInvalid();
        }

        db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = userId, Role = invite.Role, JoinedAt = now, UpdatedAt = now });
        await users.BumpAclVersionAsync([userId], cancellationToken).ConfigureAwait(false);
        foreach (var observer in observers)
        {
            await observer.OnMembershipChangedAsync(new MembershipChange(group.Id, userId, null, invite.Role, MembershipChangeKind.Joined), cancellationToken).ConfigureAwait(false);
        }

        audit.Record(
            GroupAuditActions.MemberJoined,
            GroupAuditActions.ResourceType,
            group.Id.ToString(),
            null,
            new { UserId = userId, Role = GroupRoles.Format(invite.Role), InviteId = invite.Id, Via = invite.IsLink ? "link" : "email", Automatic = auto },
            group.OwnerUserId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<(Group Group, GroupMember Actor)> LoadAsync(Guid actorId, Guid groupId, CancellationToken cancellationToken)
    {
        var actor = await db.GroupMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId, cancellationToken).ConfigureAwait(false)
            ?? throw GroupErrors.GroupNotFound();
        var group = await db.Groups.AsNoTracking().SingleAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        return (group, actor);
    }

    internal static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static object InviteState(GroupInvite invite) => new
    {
        InviteId = invite.Id,
        Kind = invite.IsLink ? "link" : "email",
        invite.Email,
        Role = GroupRoles.Format(invite.Role),
        invite.MaxUses,
        ExpiresAt = invite.ExpiresAt.ToDateTimeOffset(),
    };
}
