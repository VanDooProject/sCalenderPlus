import { createOpenApiHttp } from 'openapi-msw'
import type { paths } from '../schema'
import { authHandlers, mockAuth, sessionGuard } from './auth'
import { calendarHandlers, mockCalendars } from './calendars'
import { entitlementHandlers } from './entitlements'
import { eventHandlers } from './events'
import { feedHandlers, mockFeeds } from './feeds'
import { groupHandlers, mockGroups } from './groups'
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
 * Turns the (freshly reset) mock state into a brand-new account: no calendars, events, feed links
 * or share links, and a member of no group (the groups of others stay, so invite links still
 * work). The onboarding checklist starts from here (mock mode: `localStorage['scal.mock.scenario']
 * = 'new-user'`).
 */
export function mockNewUser(): void {
  Object.assign(mockCalendars, {
    calendars: [],
    events: [],
    series: [],
    grants: [],
    prefs: {},
    versions: {},
    overrides: {},
  })
  mockGroups.groups = mockGroups.groups.filter(
    (g) => !g.members.some((m) => m.userId === mockAuth.user.id),
  )
  Object.assign(mockFeeds, { tokens: [], shareLinks: [] })
  mockAuth.user = { ...mockAuth.user, createdAt: new Date().toISOString() }
}

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
