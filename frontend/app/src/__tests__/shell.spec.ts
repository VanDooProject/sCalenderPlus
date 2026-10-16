import { afterEach, describe, expect, it } from 'vitest'
import { http, mockEntitlements, mockUser, resetMockAuth } from '@scalenderplus/api-client/mocks'
import { trialDaysLeft } from '@/composables/entitlements'
import { sessionQueryKey } from '@/composables/session'
import { defaultConfig } from '@/config'
import { byTestId, click, mountApp, queryTestId, resetDom, settle, unmountApp } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(resetDom)

describe('routing and session', () => {
  it('sends signed-out visitors to the login page with the target as next', async () => {
    resetMockAuth({ signedIn: false })
    const { router } = await mountApp('/settings/security')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe('/settings/security')
    expect(byTestId('login-form')).toBeTruthy()
    expect(document.title).toBe('Sign in · sCalenderPlus')
  })

  it('sends signed-out visitors of the home page to a plain login', async () => {
    resetMockAuth({ signedIn: false })
    const { router } = await mountApp('/')
    expect(router.currentRoute.value.fullPath).toBe('/login')
  })

  it('opens the calendar for signed-in users and keeps them away from the login page', async () => {
    const { router } = await mountApp('/')
    expect(router.currentRoute.value.name).toBe('calendar')
    expect(byTestId('calendar-page')).toBeTruthy()

    await router.push('/login?next=/settings/profile')
    await settle()
    expect(router.currentRoute.value.name).toBe('settings-profile')
  })

  it('ignores an external next target', async () => {
    const { router } = await mountApp('/login?next=//evil.test/x')
    expect(router.currentRoute.value.name).toBe('calendar')
  })

  it('shows the user, the calendars and the environment', async () => {
    await mountApp('/calendar')

    expect(byTestId('user-menu-trigger').getAttribute('aria-label')).toContain(mockUser.displayName)
    expect(byTestId('sidebar-calendars').textContent).toContain('FC Lions – Club')
    expect(byTestId('environment-badge').textContent?.trim()).toBe('Environment: test')
    expect(queryTestId('verify-banner')).toBeNull()
  })

  it('hides the environment badge in production', async () => {
    await mountApp('/calendar', { config: { ...defaultConfig, environment: 'production' } })
    expect(queryTestId('environment-badge')).toBeNull()
  })

  it('asks unverified users to confirm their email and resends the link', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await mountApp('/calendar')

    expect(byTestId('verify-banner').textContent).toContain(mockUser.email)
    byTestId('verify-banner').querySelector('button')!.click()
    await settle()
    expect(byTestId('toast').textContent).toContain('We sent you a new confirmation link.')
  })

  it('offers a retry when the api is unreachable', async () => {
    server.use(
      http.get('/api/v1/me', ({ response }) =>
        response.untyped(new Response(null, { status: 502 })),
      ),
    )
    await mountApp('/calendar')
    expect(byTestId('api-unavailable').textContent).toContain("We can't reach the server")
  })

  it('signs out from the user menu', async () => {
    const { router, queryClient } = await mountApp('/calendar')

    byTestId('user-menu-trigger').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    await click('logout')

    expect(router.currentRoute.value.name).toBe('login')
    expect(queryClient.getQueryData(sessionQueryKey)).toBeNull()
    expect(byTestId('toast').textContent).toContain("You're signed out.")
  })

  it('renders German and switches the language from the menu', async () => {
    await mountApp('/calendar?view=month&date=2026-11-04', { locale: 'de' })
    // The heading names the page and the shown range (FullCalendar's localized title).
    expect(document.querySelector('main h1')?.textContent).toBe('Kalender: November 2026')

    byTestId('locale-switcher').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    await click('locale-option-en')

    expect(document.querySelector('main h1')?.textContent).toBe('Calendar: November 2026')
    expect(localStorage.getItem('scal.locale')).toBe('en')
    expect(document.documentElement.lang).toBe('en')
  })

  it('switches to the dark theme and remembers it', async () => {
    await mountApp('/calendar')

    byTestId('theme-toggle').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    await click('theme-option-dark')

    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(localStorage.getItem('scal.theme')).toBe('dark')

    byTestId('theme-toggle').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    await click('theme-option-system')
    expect(localStorage.getItem('scal.theme')).toBeNull()
  })

  it('sends the user to the login when the session ends', async () => {
    const { router, queryClient } = await mountApp('/settings/profile')
    resetMockAuth({ signedIn: false })

    await queryClient.refetchQueries({ queryKey: ['calendars'] })
    await settle()

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe('/settings/profile')
    expect(byTestId('toast').textContent).toContain('Your session has ended.')
  })

  it('shows a 404 page for unknown routes', async () => {
    await mountApp('/does/not/exist')
    expect(document.querySelector('h1')?.textContent).toBe('Page not found')
  })
})

describe('reverse trial notice', () => {
  const day = 24 * 60 * 60 * 1000
  const entitlements = (fields: Partial<typeof mockEntitlements>) =>
    http.get('/api/v1/me/entitlements', ({ response }) =>
      response(200).json({ ...mockEntitlements, ...fields }),
    )

  it('counts the days left, a started day as a whole one', () => {
    const now = Date.parse('2026-10-16T12:00:00Z')
    const trial = (trialEndsAt: string | null, plan = 'pro') => ({ plan, trialEndsAt })
    expect(trialDaysLeft(trial('2026-10-26T12:00:00Z'), now)).toBe(10)
    expect(trialDaysLeft(trial('2026-10-16T13:00:00Z'), now)).toBe(1)
    expect(trialDaysLeft(trial('2026-10-16T12:00:00Z'), now)).toBeNull()
    expect(trialDaysLeft(trial(null), now)).toBeNull()
    expect(trialDaysLeft(trial('2026-10-26T12:00:00Z', 'selfhost'), now)).toBeNull()
    expect(trialDaysLeft(null, now)).toBeNull()
  })

  it('shows the days left of the Pro trial until dismissed for the session', async () => {
    const trialEndsAt = new Date(Date.now() + 10 * day - 60 * 60 * 1000).toISOString()
    server.use(entitlements({ trialEndsAt }))
    await mountApp('/calendar')

    const notice = byTestId('trial-notice')
    expect(notice.querySelector('[role="status"]')?.textContent).toContain(
      'Pro trial: 10 days left',
    )
    await click('trial-notice-dismiss')
    expect(queryTestId('trial-notice')).toBeNull()

    // Still hidden after a reload in the same session.
    unmountApp()
    await mountApp('/calendar')
    expect(queryTestId('trial-notice')).toBeNull()
  })

  it('uses the singular and German plural forms', async () => {
    server.use(entitlements({ trialEndsAt: new Date(Date.now() + day / 2).toISOString() }))
    await mountApp('/calendar')
    expect(byTestId('trial-notice').textContent).toContain('Pro trial: 1 day left')

    resetDom()
    server.use(entitlements({ trialEndsAt: new Date(Date.now() + 3 * day - 1000).toISOString() }))
    await mountApp('/calendar', { locale: 'de' })
    expect(byTestId('trial-notice').textContent).toContain('Pro-Testphase: noch 3 Tage')
  })

  it('shows nothing without a trial or on self-hosted instances', async () => {
    server.use(entitlements({ trialEndsAt: null, plan: 'free' }))
    await mountApp('/calendar')
    expect(queryTestId('trial-notice')).toBeNull()

    resetDom()
    server.use(entitlements({ plan: 'selfhost' }))
    await mountApp('/calendar')
    expect(queryTestId('trial-notice')).toBeNull()
  })
})
