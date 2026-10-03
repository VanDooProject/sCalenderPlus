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
