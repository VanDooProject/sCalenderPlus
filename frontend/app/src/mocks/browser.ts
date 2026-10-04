import { http, HttpResponse } from 'msw'
import { setupWorker } from 'msw/browser'
import { handlers, resetMockAuth, resetMockGroups } from '@scalenderplus/api-client/mocks'
import type { AppConfig } from '@/config'

/** `/config.json` is served by `web` (not part of the API); in mock mode the badge shows "mock". */
const mockConfig: AppConfig = { environment: 'mock' }

/**
 * Starting session of mock mode: signed in, unless `localStorage['scal.mock.session']` says
 * `signed-out` (the mocked e2e suite sets it with an init script to start at the login page).
 */
function initialSession() {
  try {
    return localStorage.getItem('scal.mock.session') === 'signed-out'
  } catch {
    return false
  }
}

export const worker = setupWorker(
  http.get('/config.json', () => HttpResponse.json(mockConfig)),
  ...handlers,
)

export async function startMockWorker() {
  resetMockAuth({ signedIn: !initialSession() })
  resetMockGroups()
  await worker.start({ onUnhandledFrame: 'bypass', quiet: true })
}
