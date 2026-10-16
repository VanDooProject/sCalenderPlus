import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'

type EntitlementsResponse = components['schemas']['EntitlementsResponse']

/**
 * The mock user's plan: a new account in its reverse trial — Pro limits until `trialEndsAt`
 * (90 days after `mockUser.createdAt`), then Free. Tests override it per case with
 * `server.use(http.get('/api/v1/me/entitlements', …))`.
 */
export const mockEntitlements: EntitlementsResponse = {
  plan: 'pro',
  trialEndsAt: '2026-12-30T08:00:00Z',
  limits: {
    ownedCalendars: 30,
    ownedGroups: 10,
    membersPerGroup: 150,
    eventsWithOverrides: null,
    overridesPerEvent: 25,
  },
}

/** `GET /me/entitlements` (signed out: 401 from the session guard). */
export function entitlementHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  return [
    http.get('/api/v1/me/entitlements', ({ response }) => response(200).json(mockEntitlements)),
  ]
}
