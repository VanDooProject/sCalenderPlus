import { afterAll, afterEach, beforeAll } from 'vitest'
import { setupServer } from 'msw/node'
import { handlers, resetMockAuth } from '@scalenderplus/api-client/mocks'

/**
 * MSW for Vitest with the shared API handlers; call in a spec file's top level. The stateful auth
 * mocks start signed in; `resetMockAuth({ signedIn: false })` in a test starts signed out.
 */
export function useMockApi() {
  const server = setupServer(...handlers)
  beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
  afterEach(() => {
    server.resetHandlers()
    resetMockAuth()
  })
  afterAll(() => server.close())
  return server
}
