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
]
