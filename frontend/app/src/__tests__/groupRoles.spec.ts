import { describe, expect, it } from 'vitest'
import { ApiError } from '@/lib/apiError'
import { groupErrorMessage } from '@/lib/groupErrors'
import {
  assignableRoles,
  canManage,
  canRemove,
  canRevokeInvite,
  canTransferBillingTo,
  inviteRoles,
  ownerLeavingBlock,
  type MemberContext,
} from '@/lib/groupRoles'

const target = (overrides: Partial<MemberContext> = {}): MemberContext => ({
  role: 'member',
  isSelf: false,
  isBillingOwner: false,
  ownerCount: 2,
  ...overrides,
})

describe('group roles (permissions.md §6.1)', () => {
  it('owners manage everyone, admins members and viewers, others nobody', () => {
    expect(canManage('owner', 'owner')).toBe(true)
    expect(canManage('admin', 'member')).toBe(true)
    expect(canManage('admin', 'viewer')).toBe(true)
    expect(canManage('admin', 'admin')).toBe(false)
    expect(canManage('admin', 'owner')).toBe(false)
    expect(canManage('member', 'viewer')).toBe(false)
    expect(canManage('viewer', 'viewer')).toBe(false)
  })

  it('assigns roles up to the actor’s maximum; everyone may only lower their own role', () => {
    expect(assignableRoles('owner', target({ role: 'admin' }))).toEqual([
      'viewer',
      'member',
      'admin',
      'owner',
    ])
    expect(assignableRoles('admin', target({ role: 'viewer' }))).toEqual(['viewer', 'member'])
    expect(assignableRoles('admin', target({ role: 'admin' }))).toEqual([])
    expect(assignableRoles('member', target({ role: 'viewer' }))).toEqual([])
    expect(assignableRoles('admin', target({ role: 'admin', isSelf: true }))).toEqual([
      'viewer',
      'member',
      'admin',
    ])
    expect(assignableRoles('viewer', target({ role: 'viewer', isSelf: true }))).toEqual(['viewer'])
  })

  it('lets everyone leave and managers remove whom they manage', () => {
    expect(canRemove('viewer', target({ role: 'viewer', isSelf: true }))).toBe(true)
    expect(canRemove('admin', target({ role: 'member' }))).toBe(true)
    expect(canRemove('admin', target({ role: 'admin' }))).toBe(false)
    expect(canRemove('member', target({ role: 'viewer' }))).toBe(false)
  })

  it('knows the last-owner and billing-owner rules', () => {
    expect(ownerLeavingBlock(target({ role: 'owner', ownerCount: 1 }))).toBe('last_owner')
    expect(ownerLeavingBlock(target({ role: 'owner', isBillingOwner: true }))).toBe(
      'billing_owner_transfer_required',
    )
    expect(ownerLeavingBlock(target({ role: 'owner' }))).toBeNull()
    expect(ownerLeavingBlock(target({ role: 'admin', ownerCount: 1 }))).toBeNull()
  })

  it('limits invites: links at most member, admins at most member, members none', () => {
    expect(inviteRoles('owner', 'email')).toEqual(['viewer', 'member', 'admin', 'owner'])
    expect(inviteRoles('owner', 'link')).toEqual(['viewer', 'member'])
    expect(inviteRoles('admin', 'email')).toEqual(['viewer', 'member'])
    expect(inviteRoles('member', 'email')).toEqual([])
    expect(canRevokeInvite('admin', 'member')).toBe(true)
    expect(canRevokeInvite('admin', 'admin')).toBe(false)
    expect(canRevokeInvite('member', 'viewer')).toBe(false)
  })

  it('transfers billing only from the billing owner to another owner', () => {
    expect(canTransferBillingTo('owner', true, target({ role: 'owner' }))).toBe(true)
    expect(canTransferBillingTo('owner', false, target({ role: 'owner' }))).toBe(false)
    expect(canTransferBillingTo('owner', true, target({ role: 'admin' }))).toBe(false)
    expect(canTransferBillingTo('owner', true, target({ role: 'owner', isSelf: true }))).toBe(false)
  })
})

describe('group error messages', () => {
  const t = (key: string, named?: Record<string, unknown>) =>
    named ? `${key} ${JSON.stringify(named)}` : key
  const error = (code: string, status = 409) => new ApiError(status, { code })

  it('explains the owner rules in terms of the action', () => {
    expect(groupErrorMessage(t, error('last_owner'), { kind: 'leave' })).toBe(
      'groups.errors.lastOwnerLeave',
    )
    expect(
      groupErrorMessage(t, error('last_owner'), { kind: 'role', name: 'Mia', self: true }),
    ).toBe('groups.errors.lastOwnerDemoteSelf')
    expect(groupErrorMessage(t, error('last_owner'), { kind: 'remove', name: 'Ola' })).toBe(
      'groups.errors.lastOwner {"name":"Ola"}',
    )
    expect(groupErrorMessage(t, error('billing_owner_transfer_required'), { kind: 'leave' })).toBe(
      'groups.errors.billingLeave',
    )
    expect(
      groupErrorMessage(t, error('billing_owner_transfer_required'), {
        kind: 'role',
        name: 'Ola',
        self: false,
      }),
    ).toBe('groups.errors.billing {"name":"Ola"}')
    expect(groupErrorMessage(t, error('group_frozen'), { kind: 'other' })).toBe(
      'groups.errors.frozen',
    )
    expect(groupErrorMessage(t, error('insufficient_permission', 403), { kind: 'other' })).toBe(
      'groups.errors.forbidden',
    )
  })

  it('falls back to the message of the code', () => {
    expect(groupErrorMessage(t, error('precondition_failed', 412), { kind: 'other' })).toBe(
      'errors.code.precondition_failed',
    )
  })
})
