import { http, HttpResponse } from 'msw'
import { setupWorker } from 'msw/browser'
import {
  handlers,
  mockSharedCalendarBlocker,
  mockSoleOwnerBlocker,
  resetMockAccount,
  resetMockAuth,
  resetMockCalendars,
  resetMockFeeds,
  resetMockGroups,
  resetMockImports,
} from '@scalenderplus/api-client/mocks'
/**
 * `/config.json` is served by `web` (not part of the API); in mock mode the badge shows "mock".
 * `localStorage['scal.mock.brand']` (JSON) is served as its `brand`, so the mocked e2e suite can
 * check white-label branding with an init script.
 */
function mockConfig() {
  let brand: unknown = undefined
  try {
    brand = JSON.parse(localStorage.getItem('scal.mock.brand') ?? 'null') ?? undefined
  } catch {
    // Missing or malformed: no brand, the edition defaults apply.
  }
  return { environment: 'mock', brand }
}

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

/**
 * `localStorage['scal.mock.account'] = 'blocked'` (mocked e2e init script): the mock user owns a
 * group alone and shares a calendar, so deleting the account is blocked.
 */
function accountBlocked() {
  try {
    return localStorage.getItem('scal.mock.account') === 'blocked'
  } catch {
    return false
  }
}

export const worker = setupWorker(
  http.get('/config.json', () => HttpResponse.json(mockConfig())),
  ...handlers,
)

export async function startMockWorker() {
  resetMockAuth({ signedIn: !initialSession() })
  resetMockGroups()
  resetMockCalendars()
  resetMockFeeds()
  resetMockAccount(
    accountBlocked() ? { blockers: [mockSoleOwnerBlocker, mockSharedCalendarBlocker] } : {},
  )
  resetMockImports()
  await worker.start({ onUnhandledFrame: 'bypass', quiet: true })
}
