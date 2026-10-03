import { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'

/**
 * MSW handlers typed against the generated `paths`: a contract change that the mocks don't follow
 * fails `typecheck`. Shared by the app's mock mode (`pnpm --filter app dev:mock`), Vitest and the
 * Playwright `mocked` project.
 */
export const http = createOpenApiHttp<paths>()

/** The signed-in user of mock mode. */
export const mockUser: components['schemas']['MeResponse'] = {
  id: '0192f2c4-0000-7000-8000-000000000001',
  email: 'mia@example.test',
  emailVerified: true,
  displayName: 'Mia',
  locale: 'en',
  timeZone: 'Europe/Berlin',
  weekStart: 'monday',
  twoFactorEnabled: false,
  createdAt: '2026-10-01T08:00:00Z',
}

type Group = components['schemas']['GroupResponse']

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

type Member = components['schemas']['MemberResponse']

/** Members of {@link mockGroup}: one per role. */
export const mockMembers: Member[] = [
  {
    userId: mockUser.id,
    displayName: 'Mia',
    email: mockUser.email,
    role: 'owner',
    isBillingOwner: true,
  },
  {
    userId: '0192f2c4-0000-7000-8000-000000000002',
    displayName: 'Adam',
    email: 'adam@example.test',
    role: 'admin',
    isBillingOwner: false,
  },
  {
    userId: '0192f2c4-0000-7000-8000-000000000003',
    displayName: 'Max',
    email: 'max@example.test',
    role: 'member',
    isBillingOwner: false,
  },
  {
    userId: '0192f2c4-0000-7000-8000-000000000004',
    displayName: 'Vic',
    email: 'vic@example.test',
    role: 'viewer',
    isBillingOwner: false,
  },
].map((m) => ({ ...m, joinedAt: '2026-10-01T08:00:00Z', etag: `"${m.userId.slice(-4)}"` }))

type Invite = components['schemas']['InviteResponse']

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

type Calendar = components['schemas']['CalendarResponse']

/** A calendar of {@link mockGroup} (default role defaults); the mock user is its owner. */
export const mockCalendar: Calendar = {
  id: '0192f2c4-0000-7000-8000-000000000301',
  name: 'FC Lions – Club',
  description: 'Trainings and matches',
  color: '#4f46e5',
  defaultTimeZone: 'Europe/Berlin',
  owner: { type: 'group', id: mockGroup.id },
  myLevel: 'owner',
  groupRoleDefaults: { admin: 'manage', member: 'contribute', viewer: 'read' },
  creatorsManageOwnEvents: true,
  creatorsMayShareExternally: false,
  frozen: false,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
}

/** A personal calendar of the mock user. */
export const mockPersonalCalendar: Calendar = {
  ...mockCalendar,
  id: '0192f2c4-0000-7000-8000-000000000302',
  name: 'Mia',
  description: null,
  color: '#16a34a',
  owner: { type: 'user', id: mockUser.id },
  groupRoleDefaults: null,
}

const mockCalendars = [mockCalendar, mockPersonalCalendar]

type Grant = components['schemas']['GrantResponse']

/** A grant on {@link mockCalendar}: a user outside the group may edit it. */
export const mockGrant: Grant = {
  id: '0192f2c4-0000-7000-8000-000000000401',
  calendarId: mockCalendar.id,
  principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000005', minRole: null },
  principalName: 'Eve',
  level: 'edit',
  createdBy: mockUser.id,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  etag: '"grant-0401"',
}

export const handlers = [
  http.get('/health/live', ({ response }) => response(200).json({ status: 'Healthy', checks: {} })),
  http.get('/health/ready', ({ response }) =>
    response(200).json({ status: 'Healthy', checks: { database: 'Healthy' } }),
  ),
  http.get('/api/v1/me', ({ response }) => response(200).json(mockUser)),
  http.patch('/api/v1/me', async ({ request, response }) => {
    const patch = (await request.json()) as Partial<typeof mockUser>
    const updated = { ...mockUser }
    for (const key of ['displayName', 'locale', 'timeZone', 'weekStart'] as const) {
      if (typeof patch[key] === 'string') updated[key] = patch[key]
    }
    return response(200).json(updated)
  }),
  http.post('/api/v1/auth/login', ({ response }) =>
    response(200).json({ twoFactorRequired: false, user: mockUser }),
  ),
  http.post('/api/v1/auth/logout', ({ response }) => response(204).empty()),

  http.get('/api/v1/groups', ({ response }) =>
    response(200).json({ items: [mockGroup], nextCursor: null }),
  ),
  http.post('/api/v1/groups', async ({ request, response }) => {
    const body = await request.json()
    return response(201).json({
      ...mockGroup,
      id: crypto.randomUUID(),
      name: body.name,
      description: body.description ?? null,
      memberCount: 1,
    })
  }),
  http.get('/api/v1/groups/{id}', ({ params, response }) =>
    params.id === mockGroup.id
      ? response(200).json(mockGroup)
      : response('default').json(notFound(`/api/v1/groups/${params.id}`), { status: 404 }),
  ),
  http.patch('/api/v1/groups/{id}', async ({ request, response }) => {
    const patch = (await request.json()) as Partial<Group>
    return response(200).json({
      ...mockGroup,
      name: patch.name ?? mockGroup.name,
      description: patch.description === '' ? null : (patch.description ?? mockGroup.description),
      memberListVisibility: patch.memberListVisibility ?? mockGroup.memberListVisibility,
    })
  }),
  http.delete('/api/v1/groups/{id}', ({ response }) => response(204).empty()),
  http.get('/api/v1/groups/{id}/members', ({ response }) =>
    response(200).json({ items: mockMembers, nextCursor: null }),
  ),
  http.patch('/api/v1/groups/{id}/members/{userId}', async ({ params, request, response }) => {
    const member = mockMembers.find((m) => m.userId === params.userId)
    if (!member) {
      return response('default').json(
        notFound(`/api/v1/groups/${params.id}/members/${params.userId}`),
        {
          status: 404,
        },
      )
    }
    const { role } = await request.json()
    return response(200).json({ ...member, role })
  }),
  http.delete('/api/v1/groups/{id}/members/{userId}', ({ response }) => response(204).empty()),
  http.post('/api/v1/groups/{id}/transfer', async ({ request, response }) => {
    const { userId } = await request.json()
    return response(200).json({ ...mockGroup, billingOwnerId: userId })
  }),
  http.get('/api/v1/groups/{id}/invites', ({ response }) =>
    response(200).json({ items: [mockInvite], nextCursor: null }),
  ),
  http.post('/api/v1/groups/{id}/invites', async ({ params, request, response }) => {
    const body = await request.json()
    const email = body.email ?? null
    const invite: Invite = {
      ...mockInvite,
      id: crypto.randomUUID(),
      groupId: params.id,
      kind: email ? 'email' : 'link',
      email,
      role: body.role,
      maxUses: email ? 1 : (body.maxUses ?? 50),
      uses: 0,
    }
    return response(201).json({
      invite,
      url: email ? null : 'http://localhost:5173/invite?token=mock-invite-token',
    })
  }),
  http.delete('/api/v1/invites/{id}', ({ response }) => response(204).empty()),
  http.post('/api/v1/invites/accept', ({ response }) =>
    response(200).json({ ...mockGroup, myRole: 'member' }),
  ),

  http.get('/api/v1/calendars', ({ response }) =>
    response(200).json({ items: mockCalendars, nextCursor: null }),
  ),
  http.post('/api/v1/calendars', async ({ request, response }) => {
    const body = await request.json()
    const groupId = body.groupId ?? null
    return response(201).json({
      ...mockCalendar,
      id: crypto.randomUUID(),
      name: body.name,
      description: body.description ?? null,
      color: body.color?.toLowerCase() ?? '#4f46e5',
      defaultTimeZone: body.defaultTimeZone,
      owner: groupId ? { type: 'group', id: groupId } : { type: 'user', id: mockUser.id },
      groupRoleDefaults: groupId
        ? {
            admin: body.groupRoleDefaults?.admin ?? 'manage',
            member: body.groupRoleDefaults?.member ?? 'contribute',
            viewer: body.groupRoleDefaults?.viewer ?? 'read',
          }
        : null,
      creatorsManageOwnEvents: body.creatorsManageOwnEvents ?? true,
      creatorsMayShareExternally: body.creatorsMayShareExternally ?? false,
    })
  }),
  http.get('/api/v1/calendars/{id}', ({ params, response }) => {
    const calendar = mockCalendars.find((c) => c.id === params.id)
    return calendar
      ? response(200).json(calendar)
      : response('default').json(notFound(`/api/v1/calendars/${params.id}`), { status: 404 })
  }),
  http.patch('/api/v1/calendars/{id}', async ({ params, request, response }) => {
    const calendar = mockCalendars.find((c) => c.id === params.id)
    if (!calendar) {
      return response('default').json(notFound(`/api/v1/calendars/${params.id}`), { status: 404 })
    }
    const patch = (await request.json()) as components['schemas']['UpdateCalendarRequest']
    const defaults = calendar.groupRoleDefaults
    return response(200).json({
      ...calendar,
      name: patch.name ?? calendar.name,
      description: patch.description === '' ? null : (patch.description ?? calendar.description),
      color: patch.color?.toLowerCase() ?? calendar.color,
      defaultTimeZone: patch.defaultTimeZone ?? calendar.defaultTimeZone,
      creatorsManageOwnEvents: patch.creatorsManageOwnEvents ?? calendar.creatorsManageOwnEvents,
      creatorsMayShareExternally:
        patch.creatorsMayShareExternally ?? calendar.creatorsMayShareExternally,
      groupRoleDefaults: defaults && {
        admin: patch.groupRoleDefaults?.admin ?? defaults.admin,
        member: patch.groupRoleDefaults?.member ?? defaults.member,
        viewer: patch.groupRoleDefaults?.viewer ?? defaults.viewer,
      },
    })
  }),
  http.delete('/api/v1/calendars/{id}', ({ response }) => response(204).empty()),
  http.get('/api/v1/calendars/{id}/grants', ({ params, response }) =>
    response(200).json({
      items: params.id === mockCalendar.id ? [mockGrant] : [],
      nextCursor: null,
    }),
  ),
  http.post('/api/v1/calendars/{id}/grants', async ({ params, request, response }) => {
    const body = await request.json()
    const minRole = body.principal.type === 'group' ? (body.principal.minRole ?? 'viewer') : null
    return response(201).json({
      ...mockGrant,
      id: crypto.randomUUID(),
      calendarId: params.id,
      principal: { type: body.principal.type, id: body.principal.id, minRole },
      principalName: body.principal.type === 'group' ? mockGroup.name : 'Max',
      level: body.level,
    })
  }),
  http.patch('/api/v1/calendars/{id}/grants/{grantId}', async ({ params, request, response }) => {
    if (params.grantId !== mockGrant.id) {
      return response('default').json(
        notFound(`/api/v1/calendars/${params.id}/grants/${params.grantId}`),
        { status: 404 },
      )
    }
    const { level } = await request.json()
    return response(200).json({ ...mockGrant, level: level ?? mockGrant.level })
  }),
  http.delete('/api/v1/calendars/{id}/grants/{grantId}', ({ response }) => response(204).empty()),
]

function notFound(instance: string): components['schemas']['ProblemDetails'] {
  return {
    type: 'https://scalenderplus.app/problems/not-found',
    title: 'Not found',
    status: 404,
    code: 'not_found',
    instance,
    traceId: '00-mock',
  }
}
