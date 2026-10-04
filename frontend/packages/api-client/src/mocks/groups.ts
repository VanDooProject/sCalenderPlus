import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import { mockAuth, mockProblem, mockUser } from './auth'

type Group = components['schemas']['GroupResponse']
type Member = components['schemas']['MemberResponse']
type Invite = components['schemas']['InviteResponse']
type ErrorCode = components['schemas']['ErrorCode']
type Role = 'viewer' | 'member' | 'admin' | 'owner'

const rank: Record<Role, number> = { viewer: 0, member: 1, admin: 2, owner: 3 }
const isRole = (value: unknown): value is Role => typeof value === 'string' && value in rank

/** A group of mock mode in which the mock user is owner (and billing owner). */
export const mockGroup: Group = {
  id: '0192f2c4-0000-7000-8000-000000000101',
  name: 'FC Lions',
  description: 'Youth football club',
  myRole: 'owner',
  billingOwnerId: mockUser.id,
  memberCount: 4,
  memberListVisibility: 'all_members',
  frozen: false,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
}

/** A group in which the mock user is a plain member (another user is owner and billing owner). */
export const mockMemberGroup: Group = {
  ...mockGroup,
  id: '0192f2c4-0000-7000-8000-000000000102',
  name: 'Book club',
  description: null,
  myRole: 'member',
  billingOwnerId: '0192f2c4-0000-7000-8000-000000000006',
  memberCount: 2,
}

/** A group the mock user is not in; the invite token {@link mockInviteTokens.valid} leads there. */
export const mockInvitedGroup: Group = {
  ...mockGroup,
  id: '0192f2c4-0000-7000-8000-000000000103',
  name: 'Choir',
  description: 'Thursday rehearsals',
  myRole: 'member',
  billingOwnerId: '0192f2c4-0000-7000-8000-000000000007',
  memberCount: 1,
}

const joinedAt = '2026-10-01T08:00:00Z'

/** Members of {@link mockGroup}: one per role (the mock user is the owner). */
export const mockMembers: Member[] = [
  { userId: mockUser.id, displayName: 'Mia', email: mockUser.email, role: 'owner' },
  {
    userId: '0192f2c4-0000-7000-8000-000000000002',
    displayName: 'Adam',
    email: 'adam@example.test',
    role: 'admin',
  },
  {
    userId: '0192f2c4-0000-7000-8000-000000000003',
    displayName: 'Max',
    email: 'max@example.test',
    role: 'member',
  },
  {
    userId: '0192f2c4-0000-7000-8000-000000000004',
    displayName: 'Vic',
    email: 'vic@example.test',
    role: 'viewer',
  },
].map((m) => ({
  ...m,
  isBillingOwner: m.userId === mockUser.id,
  joinedAt,
  etag: `"${m.userId.slice(-4)}-1"`,
}))

/** A pending invite link of {@link mockGroup}. */
export const mockInvite: Invite = {
  id: '0192f2c4-0000-7000-8000-000000000201',
  groupId: mockGroup.id,
  kind: 'link',
  email: null,
  role: 'member',
  maxUses: 50,
  uses: 3,
  expiresAt: '2026-10-08T08:00:00Z',
  createdBy: mockUser.id,
  createdAt: '2026-10-01T08:00:00Z',
}

/**
 * Invite tokens the mocks know (`/invite?token=…`); links created in mock mode get their own
 * `mock-invite-<n>` tokens that work the same way.
 */
export const mockInviteTokens = {
  /** A link to {@link mockInvitedGroup} (role member) by Olga. */
  valid: 'mock-invite-token',
  /** Unknown, expired, revoked or used up: `400 token_invalid`. */
  invalid: 'mock-invite-expired',
  /** An email invite for another address: the preview works, accepting is `invite_email_mismatch`. */
  otherEmail: 'mock-invite-other-email',
  /** A link to a frozen group: the preview works, accepting is `409 group_frozen`. */
  frozen: 'mock-invite-frozen',
} as const

interface MemberRecord {
  userId: string
  displayName: string
  email: string
  role: Role
  joinedAt: string
  version: number
}

interface InviteRecord {
  invite: Invite
  token: string
  revoked: boolean
  inviterName: string
}

interface GroupRecord {
  id: string
  name: string
  description: string | null
  billingOwnerId: string
  memberListVisibility: string
  frozen: boolean
  createdAt: string
  updatedAt: string
  version: number
  members: MemberRecord[]
  invites: InviteRecord[]
}

export interface MockGroupsState {
  groups: GroupRecord[]
  /** Groups with a pending invite the mock user may join (tokens of other groups). */
  nextId: number
}

function member(userId: string, displayName: string, role: Role): MemberRecord {
  return {
    userId,
    displayName,
    email: `${displayName.toLowerCase()}@example.test`,
    role,
    joinedAt,
    version: 1,
  }
}

function initialState(): MockGroupsState {
  const lions: GroupRecord = {
    id: mockGroup.id,
    name: mockGroup.name,
    description: mockGroup.description,
    billingOwnerId: mockUser.id,
    memberListVisibility: 'all_members',
    frozen: false,
    createdAt: mockGroup.createdAt,
    updatedAt: mockGroup.updatedAt,
    version: 1,
    members: mockMembers.map((m) => ({
      userId: m.userId,
      displayName: m.displayName,
      email: m.email ?? '',
      role: m.role as Role,
      joinedAt,
      version: 1,
    })),
    invites: [
      {
        invite: { ...mockInvite },
        token: 'mock-invite-lions',
        revoked: false,
        inviterName: 'Mia',
      },
    ],
  }
  const books: GroupRecord = {
    ...lions,
    id: mockMemberGroup.id,
    name: mockMemberGroup.name,
    description: null,
    billingOwnerId: mockMemberGroup.billingOwnerId,
    members: [
      member(mockMemberGroup.billingOwnerId, 'Olga', 'owner'),
      { ...member(mockUser.id, 'Mia', 'member'), email: mockUser.email },
    ],
    invites: [],
  }
  const linkTo = (group: GroupRecord, id: string, token: string): InviteRecord => ({
    invite: {
      ...mockInvite,
      id,
      groupId: group.id,
      uses: 0,
      createdBy: group.billingOwnerId,
    },
    token,
    revoked: false,
    inviterName: 'Olga',
  })
  const choir: GroupRecord = {
    ...lions,
    id: mockInvitedGroup.id,
    name: mockInvitedGroup.name,
    description: mockInvitedGroup.description,
    billingOwnerId: mockInvitedGroup.billingOwnerId,
    members: [member(mockInvitedGroup.billingOwnerId, 'Olga', 'owner')],
    invites: [],
  }
  choir.invites.push(
    linkTo(choir, '0192f2c4-0000-7000-8000-000000000202', mockInviteTokens.valid),
    {
      ...linkTo(choir, '0192f2c4-0000-7000-8000-000000000203', mockInviteTokens.otherEmail),
      invite: {
        ...linkTo(choir, '0192f2c4-0000-7000-8000-000000000203', '').invite,
        kind: 'email',
        email: 'someone-else@example.test',
        maxUses: 1,
      },
    },
  )
  const frozen: GroupRecord = {
    ...choir,
    id: '0192f2c4-0000-7000-8000-000000000104',
    name: 'Frozen five',
    description: null,
    frozen: true,
    members: [member(mockInvitedGroup.billingOwnerId, 'Olga', 'owner')],
    invites: [],
  }
  frozen.invites.push(
    linkTo(frozen, '0192f2c4-0000-7000-8000-000000000204', mockInviteTokens.frozen),
  )
  return { groups: [lions, books, choir, frozen], nextId: 1 }
}

/** Mutable groups, members and invites of the mocks (reset per test with {@link resetMockGroups}). */
export const mockGroups: MockGroupsState = initialState()

export function resetMockGroups(): void {
  Object.assign(mockGroups, initialState())
}

const me = () => mockAuth.user.id
const membership = (group: GroupRecord, userId = me()) =>
  group.members.find((m) => m.userId === userId)
const groupEtag = (group: GroupRecord) => `"group-${group.id.slice(-4)}-${group.version}"`
const memberEtag = (m: MemberRecord) => `"${m.userId.slice(-4)}-${m.version}"`

function groupResponse(group: GroupRecord): Group {
  return {
    id: group.id,
    name: group.name,
    description: group.description,
    myRole: membership(group)?.role ?? 'member',
    billingOwnerId: group.billingOwnerId,
    memberCount: group.members.length,
    memberListVisibility: group.memberListVisibility,
    frozen: group.frozen,
    createdAt: group.createdAt,
    updatedAt: group.updatedAt,
  }
}

function memberResponse(group: GroupRecord, m: MemberRecord, viewer: Role): Member {
  return {
    userId: m.userId,
    displayName: m.displayName,
    email: rank[viewer] >= rank.admin ? m.email : null,
    role: m.role,
    isBillingOwner: group.billingOwnerId === m.userId,
    joinedAt: m.joinedAt,
    etag: memberEtag(m),
  }
}

function touch(group: GroupRecord) {
  group.version += 1
  group.updatedAt = new Date(Date.parse(group.updatedAt) + 60_000).toISOString()
}

const pending = (record: InviteRecord) =>
  !record.revoked &&
  record.invite.uses < record.invite.maxUses &&
  Date.parse(record.invite.expiresAt) > Date.parse('2026-10-02T00:00:00Z')

function findInvite(token: string) {
  for (const group of mockGroups.groups) {
    const record = group.invites.find((i) => i.token === token.trim())
    if (record && pending(record)) return { group, record }
  }
  return undefined
}

/** Role-change and removal rules of `Core/Groups/MembershipPolicy` (permissions.md §6.1). */
function canManage(actor: Role, target: Role) {
  return actor === 'owner' || (actor === 'admin' && rank[target] <= rank.member)
}
const maxAssignable = (actor: Role): Role | null =>
  actor === 'owner' ? 'owner' : actor === 'admin' ? 'member' : null

function ownerLeaving(group: GroupRecord, target: MemberRecord): ErrorCode | null {
  if (group.members.filter((m) => m.role === 'owner').length <= 1) return 'last_owner'
  return group.billingOwnerId === target.userId ? 'billing_owner_transfer_required' : null
}

const statusOf: Partial<Record<ErrorCode, number>> = {
  insufficient_permission: 403,
  invite_email_mismatch: 403,
  email_not_verified: 403,
  last_owner: 409,
  billing_owner_transfer_required: 409,
  billing_owner_must_be_owner: 409,
  group_frozen: 409,
  group_has_calendars: 409,
  token_invalid: 400,
  validation_failed: 400,
  not_found: 404,
  precondition_failed: 412,
  precondition_required: 428,
}

/** Stateful handlers for `/groups*` and `/invites*` (rules as in api.md "Groups"). */
export function groupHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  const problem = (code: ErrorCode, instance: string, extras = {}) =>
    mockProblem(code, statusOf[code] ?? 400, instance, extras)

  /** The group if the mock user is a member (else a 404 problem, like the api). */
  function load(id: string, instance: string) {
    const group = mockGroups.groups.find((g) => g.id === id)
    const mine = group && membership(group)
    return group && mine
      ? { group, role: mine.role, error: null }
      : { group: null, role: null, error: problem('not_found', instance) }
  }

  /** `If-Match` check: missing → 428, stale → 412. */
  function precondition(request: Request, current: string, instance: string) {
    const ifMatch = request.headers.get('If-Match')
    if (!ifMatch) return problem('precondition_required', instance)
    if (ifMatch !== '*' && ifMatch !== current) return problem('precondition_failed', instance)
    return null
  }

  return [
    http.get('/api/v1/groups', ({ response }) =>
      response(200).json({
        items: mockGroups.groups.filter((g) => membership(g)).map(groupResponse),
        nextCursor: null,
      }),
    ),
    http.post('/api/v1/groups', async ({ request, response }) => {
      const body = await request.json()
      const name = body.name.trim()
      if (!name) {
        return response('default').json(
          problem('validation_failed', '/api/v1/groups', { errors: { name: ['Required.'] } }),
          { status: 400 },
        )
      }
      const now = new Date().toISOString()
      const group: GroupRecord = {
        id: crypto.randomUUID(),
        name,
        description: body.description?.trim() || null,
        billingOwnerId: me(),
        memberListVisibility: 'all_members',
        frozen: false,
        createdAt: now,
        updatedAt: now,
        version: 1,
        members: [
          {
            userId: me(),
            displayName: mockAuth.user.displayName,
            email: mockAuth.user.email,
            role: 'owner',
            joinedAt: now,
            version: 1,
          },
        ],
        invites: [],
      }
      mockGroups.groups.push(group)
      return response(201).json(groupResponse(group), {
        headers: { ETag: groupEtag(group), Location: `/api/v1/groups/${group.id}` },
      })
    }),
    http.get('/api/v1/groups/{id}', ({ params, response }) => {
      const { group, error } = load(params.id, `/api/v1/groups/${params.id}`)
      if (!group) return response('default').json(error, { status: 404 })
      return response(200).json(groupResponse(group), { headers: { ETag: groupEtag(group) } })
    }),
    http.patch('/api/v1/groups/{id}', async ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}`
      const { group, role, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (rank[role] < rank.admin) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const failed = precondition(request, groupEtag(group), instance)
      if (failed) return response('default').json(failed, { status: failed.status as 412 | 428 })
      const patch = (await request.json()) as components['schemas']['UpdateGroupRequest']
      if (patch.name != null && !patch.name.trim()) {
        return response('default').json(
          problem('validation_failed', instance, { errors: { name: ['Required.'] } }),
          { status: 400 },
        )
      }
      if (patch.name != null) group.name = patch.name.trim()
      if (patch.description != null) group.description = patch.description.trim() || null
      if (patch.memberListVisibility != null) {
        group.memberListVisibility = patch.memberListVisibility
      }
      touch(group)
      return response(200).json(groupResponse(group), { headers: { ETag: groupEtag(group) } })
    }),
    http.delete('/api/v1/groups/{id}', ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}`
      const { group, role, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (role !== 'owner') {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const failed = precondition(request, groupEtag(group), instance)
      if (failed) return response('default').json(failed, { status: failed.status as 412 | 428 })
      if (group.id === mockGroup.id) {
        // FC Lions owns the mock calendar "FC Lions – Club".
        return response('default').json(
          problem('group_has_calendars', instance, { calendarCount: 1 }),
          { status: 409 },
        )
      }
      mockGroups.groups = mockGroups.groups.filter((g) => g !== group)
      return response(204).empty()
    }),

    http.get('/api/v1/groups/{id}/members', ({ params, response }) => {
      const instance = `/api/v1/groups/${params.id}/members`
      const { group, role, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (group.memberListVisibility === 'members_and_above' && role === 'viewer') {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const items = [...group.members]
        .sort((a, b) => a.userId.localeCompare(b.userId))
        .map((m) => memberResponse(group, m, role))
      return response(200).json({ items, nextCursor: null })
    }),
    http.patch('/api/v1/groups/{id}/members/{userId}', async ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}/members/${params.userId}`
      const { group, role: actor, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      const target = membership(group, params.userId)
      if (!target) return response('default').json(problem('not_found', instance), { status: 404 })
      const { role } = await request.json()
      if (!isRole(role)) {
        return response('default').json(
          problem('validation_failed', instance, { errors: { role: ['Unknown role.'] } }),
          { status: 400 },
        )
      }
      const self = target.userId === me()
      const max = maxAssignable(actor)
      if (
        self
          ? rank[role] > rank[target.role]
          : !canManage(actor, target.role) || !max || rank[role] > rank[max]
      ) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const failed = precondition(request, memberEtag(target), instance)
      if (failed) return response('default').json(failed, { status: failed.status as 412 | 428 })
      if (role !== target.role) {
        if (group.frozen) {
          return response('default').json(problem('group_frozen', instance), { status: 409 })
        }
        const blocked = target.role === 'owner' ? ownerLeaving(group, target) : null
        if (blocked) return response('default').json(problem(blocked, instance), { status: 409 })
        target.role = role
        target.version += 1
        touch(group)
      }
      return response(200).json(memberResponse(group, target, membership(group)?.role ?? actor))
    }),
    http.delete('/api/v1/groups/{id}/members/{userId}', ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}/members/${params.userId}`
      const { group, role: actor, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      const target = membership(group, params.userId)
      if (!target) return response('default').json(problem('not_found', instance), { status: 404 })
      if (target.userId !== me() && !canManage(actor, target.role)) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const failed = precondition(request, memberEtag(target), instance)
      if (failed) return response('default').json(failed, { status: failed.status as 412 | 428 })
      const blocked = target.role === 'owner' ? ownerLeaving(group, target) : null
      if (blocked) return response('default').json(problem(blocked, instance), { status: 409 })
      group.members = group.members.filter((m) => m !== target)
      touch(group)
      return response(204).empty()
    }),
    http.post('/api/v1/groups/{id}/transfer', async ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}/transfer`
      const { group, role, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (role !== 'owner' || group.billingOwnerId !== me()) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      const { userId } = await request.json()
      if (membership(group, userId)?.role !== 'owner') {
        return response('default').json(problem('billing_owner_must_be_owner', instance), {
          status: 409,
        })
      }
      group.billingOwnerId = userId
      touch(group)
      return response(200).json(groupResponse(group), { headers: { ETag: groupEtag(group) } })
    }),

    http.get('/api/v1/groups/{id}/invites', ({ params, response }) => {
      const instance = `/api/v1/groups/${params.id}/invites`
      const { group, role, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (!maxAssignable(role)) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      return response(200).json({
        items: group.invites.filter(pending).map((i) => i.invite),
        nextCursor: null,
      })
    }),
    http.post('/api/v1/groups/{id}/invites', async ({ params, request, response }) => {
      const instance = `/api/v1/groups/${params.id}/invites`
      const { group, role: actor, error } = load(params.id, instance)
      if (!group) return response('default').json(error, { status: 404 })
      if (!mockAuth.user.emailVerified) {
        return response('default').json(problem('email_not_verified', instance), { status: 403 })
      }
      const body = await request.json()
      const email = body.email?.trim() || null
      const max = maxAssignable(actor)
      if (!isRole(body.role) || !max || rank[body.role] > rank[max]) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      if (!email && rank[body.role] > rank.member) {
        return response('default').json(
          problem('validation_failed', instance, {
            errors: { role: ['Invite links carry at most the member role.'] },
          }),
          { status: 400 },
        )
      }
      if (body.maxUses != null && (body.maxUses < 1 || body.maxUses > 1000)) {
        return response('default').json(
          problem('validation_failed', instance, { errors: { maxUses: ['Use 1 to 1000.'] } }),
          { status: 400 },
        )
      }
      if (group.frozen) {
        return response('default').json(problem('group_frozen', instance), { status: 409 })
      }
      if (email) {
        group.invites
          .filter((i) => i.invite.email?.toLowerCase() === email.toLowerCase())
          .forEach((i) => (i.revoked = true))
      }
      const n = mockGroups.nextId++
      const days = body.expiresInDays ?? (email ? 14 : 7)
      const invite: Invite = {
        id: `0192f2c4-0000-7000-8000-${String(900000000000 + n)}`,
        groupId: group.id,
        kind: email ? 'email' : 'link',
        email,
        role: body.role,
        maxUses: email ? 1 : (body.maxUses ?? 50),
        uses: 0,
        expiresAt: new Date(Date.parse('2026-10-02T08:00:00Z') + days * 86_400_000)
          .toISOString()
          .replace('.000Z', 'Z'),
        createdBy: me(),
        createdAt: '2026-10-02T08:00:00Z',
      }
      const token = `mock-invite-${n}`
      group.invites.push({ invite, token, revoked: false, inviterName: mockAuth.user.displayName })
      return response(201).json({
        invite,
        url: email ? null : `${location.origin}/invite?token=${token}`,
      })
    }),
    http.delete('/api/v1/invites/{id}', ({ params, response }) => {
      const instance = `/api/v1/invites/${params.id}`
      const group = mockGroups.groups.find((g) => g.invites.some((i) => i.invite.id === params.id))
      const actor = group && membership(group)?.role
      const record = group?.invites.find((i) => i.invite.id === params.id)
      if (!group || !actor || !record) {
        return response('default').json(problem('not_found', instance), { status: 404 })
      }
      const max = maxAssignable(actor)
      if (!max || rank[record.invite.role as Role] > rank[max]) {
        return response('default').json(problem('insufficient_permission', instance), {
          status: 403,
        })
      }
      record.revoked = true
      return response(204).empty()
    }),
    http.post('/api/v1/invites/preview', async ({ request, response }) => {
      const { token } = await request.json()
      const found = findInvite(token)
      if (!found) {
        return response('default').json(problem('token_invalid', '/api/v1/invites/preview'), {
          status: 400,
        })
      }
      return response(200).json({
        groupName: found.group.name,
        inviterName: found.record.inviterName,
        role: found.record.invite.role,
        expiresAt: found.record.invite.expiresAt,
      })
    }),
    http.post('/api/v1/invites/accept', async ({ request, response }) => {
      const instance = '/api/v1/invites/accept'
      const { token } = await request.json()
      const found = findInvite(token)
      if (!found) {
        return response('default').json(problem('token_invalid', instance), { status: 400 })
      }
      if (!mockAuth.user.emailVerified) {
        return response('default').json(problem('email_not_verified', instance), { status: 403 })
      }
      const { group, record } = found
      const { invite } = record
      if (invite.email && invite.email.toLowerCase() !== mockAuth.user.email.toLowerCase()) {
        return response('default').json(problem('invite_email_mismatch', instance), {
          status: 403,
        })
      }
      if (!membership(group)) {
        if (group.frozen) {
          return response('default').json(problem('group_frozen', instance), { status: 409 })
        }
        invite.uses += 1
        group.members.push({
          userId: me(),
          displayName: mockAuth.user.displayName,
          email: mockAuth.user.email,
          role: invite.role as Role,
          joinedAt: new Date().toISOString(),
          version: 1,
        })
        touch(group)
      } else if (invite.kind === 'email') {
        invite.uses += 1
      }
      return response(200).json(groupResponse(group), { headers: { ETag: groupEtag(group) } })
    }),
  ]
}
