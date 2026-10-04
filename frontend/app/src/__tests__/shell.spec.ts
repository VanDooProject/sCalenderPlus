import { afterEach, describe, expect, it } from 'vitest'
import { http, mockUser, resetMockAuth } from '@scalenderplus/api-client/mocks'
import { sessionQueryKey } from '@/composables/session'
import { byTestId, click, mountApp, queryTestId, resetDom, settle } from './app'
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
    expect(byTestId('calendar-placeholder')).toBeTruthy()

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
    await mountApp('/calendar', { config: { environment: 'production' } })
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
    await mountApp('/calendar', { locale: 'de' })
    expect(document.querySelector('main h1')?.textContent).toBe('Kalender')

    byTestId('locale-switcher').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    await click('locale-option-en')

    expect(document.querySelector('main h1')?.textContent).toBe('Calendar')
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
