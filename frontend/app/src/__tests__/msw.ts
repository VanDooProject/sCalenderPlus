import { afterAll, afterEach, beforeAll } from 'vitest'
import { setupServer } from 'msw/node'
import {
  handlers,
  resetMockAccount,
  resetMockAuth,
  resetMockCalendars,
  resetMockFeeds,
  resetMockGroups,
  resetMockImports,
} from '@scalenderplus/api-client/mocks'

/**
 * MSW for Vitest with the shared API handlers; call in a spec file's top level. The stateful auth
 * mocks start signed in (groups and invites as in `mockGroups`); `resetMockAuth({ signedIn: false })` in a test starts signed out.
 */
export function useMockApi() {
  const server = setupServer(...handlers)
  beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
  afterEach(() => {
    server.resetHandlers()
    resetMockAuth()
    resetMockGroups()
    resetMockCalendars()
    resetMockFeeds()
    resetMockAccount()
    resetMockImports()
  })
  afterAll(() => server.close())
  return server
}
