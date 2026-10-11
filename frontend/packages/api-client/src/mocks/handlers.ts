import { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import { authHandlers, mockUser, sessionGuard } from './auth'
import { calendarHandlers, mockCalendars, mockEvent } from './calendars'
import { eventHandlers } from './events'
import { groupHandlers, mockGroup } from './groups'

export {
  mockAuth,
  mockCredentials,
  mockProblem,
  mockUser,
  resetMockAuth,
  type MockAuthState,
} from './auth'
export {
  mockBusyCalendar,
  mockBusyEvent,
  mockCalendar,
  mockCalendars,
  mockEvent,
  mockFrozenCalendar,
  mockGrant,
  mockHiddenCalendarId,
  mockPersonalCalendar,
  mockPlanLimitName,
  mockCalendarEtag,
  mockReadOnlyCalendar,
  mockSeries,
  mockResolveWallTime,
  resetMockCalendars,
  type MockCalendarsState,
} from './calendars'
export { mockConflictTitle } from './events'
export {
  mockGroup,
  mockGroups,
  mockInvite,
  mockInvitedGroup,
  mockInviteTokens,
  mockMemberGroup,
  mockMembers,
  resetMockGroups,
  type MockGroupsState,
} from './groups'

/**
 * MSW handlers typed against the generated `paths`: a contract change that the mocks don't follow
 * fails `typecheck`. Shared by the app's mock mode (`pnpm --filter app dev:mock`), Vitest and the
 * Playwright `mocked` project.
 */
export const http = createOpenApiHttp<paths>()

type Override = components['schemas']['OverrideResponse']

/** The overrides of {@link mockEvent}: hidden from Vic, read-only for Eve outside the group. */
export const mockOverrides: Override[] = [
  {
    principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000004' },
    principalName: 'Vic',
    level: 'none',
    createdBy: mockUser.id,
    createdAt: '2026-10-01T08:00:00Z',
  },
  {
    principal: { type: 'everyone' },
    level: 'free_busy',
    createdBy: mockUser.id,
    createdAt: '2026-10-01T08:00:00Z',
  },
]

export const handlers = [
  sessionGuard,
  http.get('/health/live', ({ response }) => response(200).json({ status: 'Healthy', checks: {} })),
  http.get('/health/ready', ({ response }) =>
    response(200).json({ status: 'Healthy', checks: { database: 'Healthy' } }),
  ),
  ...authHandlers(http),

  ...groupHandlers(http),

  ...calendarHandlers(http),

  ...eventHandlers(http),

  http.post('/api/v1/events/{id}/move', async ({ params, request, response }) => {
    const event = mockCalendars.events.find((e) => e.id === params.id)
    if (!event) {
      return response('default').json(notFound(`/api/v1/events/${params.id}`), { status: 404 })
    }
    const { targetCalendarId } = await request.json()
    return response(200).json({ ...event, calendarId: targetCalendarId ?? event.calendarId })
  }),
  http.get('/api/v1/events/{id}/overrides', ({ params, response }) =>
    params.id === mockEvent.id
      ? response(200).json({
          eventId: mockEvent.id,
          items: mockOverrides,
          etag: '"overrides-0401"',
        })
      : response('default').json(notFound(`/api/v1/events/${params.id}/overrides`), {
          status: 404,
        }),
  ),
  http.get('/api/v1/events/{id}/access/explain', ({ params, query, response }) => {
    if (params.id !== mockEvent.id) {
      return response('default').json(notFound(`/api/v1/events/${params.id}/access/explain`), {
        status: 404,
      })
    }
    const userId = query.get('userId') ?? mockUser.id
    // The mock user is the event's creator in a group calendar where members contribute: creator floor.
    return response(200).json({
      eventId: mockEvent.id,
      calendarId: mockEvent.calendarId,
      user: { id: userId, displayName: userId === mockUser.id ? mockUser.displayName : undefined },
      calendarLevel: 'contribute',
      level: 'manage',
      steps: [
        {
          kind: 'group_role_default',
          principal: { type: 'group', id: mockGroup.id },
          principalName: mockGroup.name,
          role: 'member',
          calendarLevel: 'contribute',
        },
        { kind: 'calendar_result', calendarLevel: 'contribute' },
        { kind: 'creator_floor', eventLevel: 'manage' },
        { kind: 'result', eventLevel: 'manage' },
      ],
    })
  }),
  http.put('/api/v1/events/{id}/overrides', async ({ params, request, response }) => {
    if (params.id !== mockEvent.id) {
      return response('default').json(notFound(`/api/v1/events/${params.id}/overrides`), {
        status: 404,
      })
    }
    const { overrides } = await request.json()
    return response(200).json({
      eventId: mockEvent.id,
      items: (overrides ?? []).map((o) => ({
        principal: { type: o.principal.type, id: o.principal.id, minRole: o.principal.minRole },
        level: o.level,
        createdBy: mockUser.id,
        createdAt: '2026-10-01T08:00:00Z',
      })),
      etag: `"overrides-${crypto.randomUUID().slice(0, 8)}"`,
    })
  }),
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
