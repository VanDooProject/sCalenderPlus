import type { GroupRole } from '@scalenderplus/api-client'

/**
 * Group role rules of permissions.md §6.1 (`Core/Groups/MembershipPolicy` on the server), used only
 * to hide or disable actions the actor cannot perform. The api decides: every action still handles
 * its error responses.
 */
export const groupRoles: readonly GroupRole[] = ['viewer', 'member', 'admin', 'owner']

const rank: Record<GroupRole, number> = { viewer: 0, member: 1, admin: 2, owner: 3 }

export function isGroupRole(value: unknown): value is GroupRole {
  return typeof value === 'string' && value in rank
}

/** The role as a known role; unknown values (a newer api) count as the lowest. */
export function asRole(value: string): GroupRole {
  return isGroupRole(value) ? value : 'viewer'
}

export function atLeast(role: GroupRole, minimum: GroupRole): boolean {
  return rank[role] >= rank[minimum]
}

/** Invite links can be forwarded freely, so they carry at most `member`. */
export const maxLinkRole: GroupRole = 'member'

/** The highest role the actor may give others (role changes and invites); null = none. */
export function maxAssignableRole(actor: GroupRole): GroupRole | null {
  return actor === 'owner' ? 'owner' : actor === 'admin' ? 'member' : null
}

/** Owners manage everyone; admins manage members and viewers. */
export function canManage(actor: GroupRole, target: GroupRole): boolean {
  return actor === 'owner' || (actor === 'admin' && rank[target] <= rank.member)
}

export const canManageInvites = (actor: GroupRole) => maxAssignableRole(actor) !== null
export const canEditGroup = (actor: GroupRole) => atLeast(actor, 'admin')
export const canDeleteGroup = (actor: GroupRole) => actor === 'owner'

/** Roles an invite may carry: up to what the actor assigns, links at most `member`. */
export function inviteRoles(actor: GroupRole, kind: 'email' | 'link'): GroupRole[] {
  const max = maxAssignableRole(actor)
  if (!max) return []
  const limit = kind === 'link' && rank[max] > rank[maxLinkRole] ? maxLinkRole : max
  return groupRoles.filter((role) => rank[role] <= rank[limit])
}

/** Whether the actor may revoke an invite with this role (whoever could have created it). */
export function canRevokeInvite(actor: GroupRole, inviteRole: GroupRole): boolean {
  const max = maxAssignableRole(actor)
  return max !== null && rank[inviteRole] <= rank[max]
}

export interface MemberContext {
  role: GroupRole
  isSelf: boolean
  isBillingOwner: boolean
  /** Owners of the group (including the target, if an owner). */
  ownerCount: number
}

/** Why the target cannot stop being an owner: last owner, or billing owner (transfer first). */
export function ownerLeavingBlock(
  target: MemberContext,
): 'last_owner' | 'billing_owner_transfer_required' | null {
  if (target.role !== 'owner') return null
  if (target.ownerCount <= 1) return 'last_owner'
  return target.isBillingOwner ? 'billing_owner_transfer_required' : null
}

/**
 * Roles the actor may give the target, the current one included (empty: no role change at all).
 * Everyone may lower their own role; others need {@link canManage} and at most the assignable role.
 */
export function assignableRoles(actor: GroupRole, target: MemberContext): GroupRole[] {
  if (target.isSelf) return groupRoles.filter((role) => rank[role] <= rank[target.role])
  const max = maxAssignableRole(actor)
  if (!max || !canManage(actor, target.role)) return []
  return groupRoles.filter((role) => rank[role] <= rank[max])
}

/** Whether the actor may remove the target (or leave, for themselves), not counting owner rules. */
export function canRemove(actor: GroupRole, target: MemberContext): boolean {
  return target.isSelf || canManage(actor, target.role)
}

/** Billing goes from the billing owner to another member with role owner. */
export function canTransferBillingTo(
  actor: GroupRole,
  actorIsBillingOwner: boolean,
  target: MemberContext,
): boolean {
  return actor === 'owner' && actorIsBillingOwner && !target.isSelf && target.role === 'owner'
}
