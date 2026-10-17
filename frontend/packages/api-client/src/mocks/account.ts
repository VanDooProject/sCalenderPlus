import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import { mockAuth, mockCredentials, mockProblem } from './auth'

type Blocker = components['schemas']['AccountDeletionBlocker']
type Deletion = components['schemas']['AccountDeletionResponse']

const graceDays = 14
const day = 86_400_000

/** Account deletion state of the mocks: when it was requested, and what blocks it (tests set blockers). */
export interface MockAccountState {
  requestedAt: string | null
  blockers: Blocker[]
}

/** A blocker the tests can put in: the mock user is the only owner of a group with members. */
export const mockSoleOwnerBlocker: Blocker = {
  type: 'group',
  id: '0192f2c4-0000-7000-8000-000000000101', // "FC Lions" of the group mocks
  name: 'FC Lions',
  reason: 'sole_owner',
  count: 3,
}

/** A blocker the tests can put in: a personal calendar of the mock user shared with others. */
export const mockSharedCalendarBlocker: Blocker = {
  type: 'calendar',
  id: '0192f2c4-0000-7000-8000-000000000302', // "Mia" of the calendar mocks
  name: 'Mia',
  reason: 'shared',
  count: 2,
}

export const mockAccount: MockAccountState = { requestedAt: null, blockers: [] }

function purgeAt(): string | null {
  return mockAccount.requestedAt
    ? new Date(Date.parse(mockAccount.requestedAt) + graceDays * day).toISOString()
    : null
}

/** Resets the deletion state (and the user's `deletionScheduledFor`), e.g. `{ blockers: [mockSoleOwnerBlocker] }`. */
export function resetMockAccount(state: Partial<MockAccountState> = {}): void {
  Object.assign(mockAccount, { requestedAt: null, blockers: [] }, state)
  mockAuth.user = { ...mockAuth.user, deletionScheduledFor: purgeAt() }
}

function state(): Deletion {
  const purge = purgeAt()
  return {
    scheduled:
      mockAccount.requestedAt && purge
        ? { requestedAt: mockAccount.requestedAt, purgeAt: purge }
        : null,
    graceDays,
    blockers: mockAccount.requestedAt ? [] : mockAccount.blockers,
  }
}

function setScheduled(requestedAt: string | null): void {
  mockAccount.requestedAt = requestedAt
  mockAuth.user = { ...mockAuth.user, deletionScheduledFor: purgeAt() }
  mockAuth.version += 1
}

/**
 * `GET /me/deletion`, `DELETE /me` (password or authenticator code; `409 account_deletion_blocked`
 * with the blockers) and `POST /me/deletion/cancel`, like the api.
 */
export function accountHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  return [
    http.get('/api/v1/me/deletion', ({ response }) => response(200).json(state())),

    http.delete('/api/v1/me', async ({ request, response }) => {
      const instance = '/api/v1/me'
      if (mockAccount.requestedAt) return response(200).json(state())
      if (mockAccount.blockers.length > 0) {
        return response('default').json(
          mockProblem('account_deletion_blocked', 409, instance, {
            blockers: mockAccount.blockers,
          }),
          { status: 409 },
        )
      }
      const body = await request.json()
      const confirmed =
        body.password === mockCredentials.password || body.code === mockCredentials.totpCode
      if (!confirmed) {
        return response('default').json(mockProblem('reauthentication_failed', 403, instance), {
          status: 403,
        })
      }
      setScheduled(new Date().toISOString())
      return response(200).json(state())
    }),

    http.post('/api/v1/me/deletion/cancel', ({ response }) => {
      setScheduled(null)
      return response(200).json(state())
    }),
  ]
}
