import { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import { authHandlers, sessionGuard } from './auth'
import { calendarHandlers, mockCalendars } from './calendars'
import { eventHandlers } from './events'
import { groupHandlers } from './groups'
import { overrideHandlers } from './overrides'

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
export { mockExplain, mockOverrideLimit } from './overrides'
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
  ...overrideHandlers(http),
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
