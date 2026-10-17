import { createOpenApiHttp } from 'openapi-msw'
import type { paths } from '../schema'
import { authHandlers, sessionGuard } from './auth'
import { calendarHandlers } from './calendars'
import { entitlementHandlers } from './entitlements'
import { eventHandlers } from './events'
import { feedHandlers } from './feeds'
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
  mockOccurrences,
  mockReadOnlyCalendar,
  mockSeries,
  mockResolveWallTime,
  resetMockCalendars,
  type MockCalendarsState,
} from './calendars'
export { mockEntitlements } from './entitlements'
export { mockConflictTitle } from './events'
export {
  mockFeedToken,
  mockFeeds,
  mockResetFeedToken,
  mockShareLink,
  resetMockFeeds,
  type MockFeedsState,
} from './feeds'
export { mockExplain, mockOverrideLimit } from './overrides'
export {
  mockFullGroupEmail,
  mockGroup,
  mockGroupLimitName,
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
  ...entitlementHandlers(http),

  ...groupHandlers(http),

  ...calendarHandlers(http),

  ...eventHandlers(http),

  ...overrideHandlers(http),

  ...feedHandlers(http),
]
