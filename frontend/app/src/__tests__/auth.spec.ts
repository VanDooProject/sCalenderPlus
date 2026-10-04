import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  http,
  mockAuth,
  mockCredentials,
  mockUser,
  resetMockAuth,
} from '@scalenderplus/api-client/mocks'
import { sessionQueryKey, type Session } from '@/composables/session'
import { byTestId, click, mountApp, queryTestId, resetDom, settle, submit, type } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

beforeEach(() => resetMockAuth({ signedIn: false }))
afterEach(resetDom)

const formError = () => queryTestId('form-error')?.textContent?.trim() ?? null
const fieldErrors = () =>
  [...document.querySelectorAll('[data-testid="field-error"]')].map((e) => e.textContent?.trim())

describe('login', () => {
  it('signs in and continues to next', async () => {
    const { router, queryClient } = await mountApp('/login?next=/settings/security')

    await type('login-email', 'mia@example.test')
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/settings/security'))
    expect(queryClient.getQueryData<Session>(sessionQueryKey)?.user.email).toBe('mia@example.test')
  })

  it('validates on the client before calling the api', async () => {
    let calls = 0
    server.events.on('request:start', () => calls++)
    await mountApp('/login')
    calls = 0

    await type('login-email', 'not-an-email')
    await submit('login-form')

    expect(fieldErrors()).toEqual(['Enter a valid email address.', 'Please fill in this field.'])
    expect(document.activeElement).toBe(byTestId('login-email'))
    expect(calls).toBe(0)
    server.events.removeAllListeners()
  })

  it('shows wrong credentials as one message', async () => {
    await mountApp('/login')
    await type('login-email', 'mia@example.test')
    await type('login-password', 'wrong password')
    await submit('login-form')

    expect(formError()).toBe('Email address or password is incorrect.')
    expect(byTestId('form-error').getAttribute('role')).toBe('alert')
  })

  it('tells how long to wait when rate limited', async () => {
    await mountApp('/login')
    await type('login-email', mockCredentials.rateLimitedEmail)
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    expect(formError()).toBe(
      `Too many attempts. Please try again in ${mockCredentials.retryAfterSeconds} seconds.`,
    )
  })

  it('reports a missing connection', async () => {
    server.use(http.post('/api/v1/auth/login', () => Response.error()))
    await mountApp('/login')
    await type('login-email', 'mia@example.test')
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    expect(formError()).toMatch(/No connection to the server/)
  })

  it('asks for the second factor and accepts an authenticator code', async () => {
    const { router } = await mountApp('/login')
    await type('login-email', mockCredentials.twoFactorEmail)
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    expect(byTestId('two-factor-form')).toBeTruthy()
    expect(document.activeElement).toBe(byTestId('two-factor-code'))

    await type('two-factor-code', '000000')
    await submit('two-factor-form')
    expect(formError()).toBe('Email address or password is incorrect.')

    await type('two-factor-code', mockCredentials.totpCode)
    await submit('two-factor-form')
    expect(router.currentRoute.value.name).toBe('calendar')
  })

  it('accepts a recovery code instead', async () => {
    const { router } = await mountApp('/login')
    await type('login-email', mockCredentials.twoFactorEmail)
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    await click('two-factor-toggle-recovery')
    await type('two-factor-recovery', mockCredentials.recoveryCode.toLowerCase())
    await submit('two-factor-form')

    expect(router.currentRoute.value.name).toBe('calendar')
  })

  it('starts over when the pending login expired', async () => {
    await mountApp('/login')
    await type('login-email', mockCredentials.twoFactorEmail)
    await type('login-password', mockCredentials.password)
    await submit('login-form')
    mockAuth.pendingTwoFactor = false

    await type('two-factor-code', mockCredentials.totpCode)
    await submit('two-factor-form')

    expect(byTestId('login-form')).toBeTruthy()
    expect(formError()).toMatch(/took too long/)
  })

  it('applies the language of the profile after signing in', async () => {
    resetMockAuth({ signedIn: false, user: { locale: 'de' } })
    await mountApp('/login')
    await type('login-email', 'mia@example.test')
    await type('login-password', mockCredentials.password)
    await submit('login-form')

    expect(document.querySelector('main h1')?.textContent).toBe('Kalender')
  })
})

describe('register', () => {
  it('shows "check your email" and never signs in', async () => {
    let body: unknown
    server.events.on('request:start', async ({ request }) => {
      if (request.url.endsWith('/auth/register')) body = await request.clone().json()
    })
    const { router } = await mountApp('/register?next=/invite?token=abc')

    await type('register-name', ' Mia ')
    await type('register-email', 'new@example.test')
    await type('register-password', 'a long passphrase')
    await submit('register-form')
    server.events.removeAllListeners()

    expect(byTestId('register-done').textContent).toContain('new@example.test')
    expect(body).toMatchObject({ displayName: 'Mia', email: 'new@example.test', locale: 'en' })
    expect(mockAuth.signedIn).toBe(false)
    expect(byTestId('register-to-login').getAttribute('href')).toBe('/login?next=/invite?token=abc')
    expect(router.currentRoute.value.name).toBe('register')
  })

  it('checks the password length on the client', async () => {
    await mountApp('/register')
    await type('register-name', 'Mia')
    await type('register-email', 'new@example.test')
    await type('register-password', 'short')
    await submit('register-form')

    expect(fieldErrors()).toEqual(['Use at least 10 characters.'])
  })

  it('shows field errors of the problem response', async () => {
    server.use(
      http.post('/api/v1/auth/register', ({ response }) =>
        response('default').json(
          {
            type: 'https://scalenderplus.app/problems/email-domain-not-allowed',
            title: 'Email domain not allowed',
            status: 422,
            code: 'email_domain_not_allowed',
            errors: { email: ['Use a permanent email address.'] },
          },
          { status: 422 },
        ),
      ),
    )
    await mountApp('/register')
    await type('register-name', 'Mia')
    await type('register-email', 'mia@trash.test')
    await type('register-password', 'a long passphrase')
    await submit('register-form')

    expect(fieldErrors()).toEqual(['Use a permanent email address.'])
    expect(formError()).toBe('Please check the highlighted fields.')
    expect(document.activeElement).toBe(byTestId('register-email'))
  })
})

describe('password reset', () => {
  it('requests a link without revealing whether the account exists', async () => {
    await mountApp('/forgot-password')
    await type('forgot-email', 'someone@example.test')
    await submit('forgot-form')

    expect(byTestId('forgot-done').textContent).toContain('someone@example.test')
  })

  it('sets a new password from the link', async () => {
    const { router } = await mountApp(
      `/reset-password?userId=${mockUser.id}&token=${mockCredentials.linkToken}`,
    )
    await type('reset-password', 'a new passphrase')
    await type('reset-confirm', 'a different one')
    await submit('reset-form')
    expect(fieldErrors()).toEqual(["The passwords don't match."])

    await type('reset-confirm', 'a new passphrase')
    await submit('reset-form')
    expect(byTestId('reset-done')).toBeTruthy()

    await click('reset-to-login')
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('explains an invalid or used link', async () => {
    await mountApp(`/reset-password?userId=${mockUser.id}&token=used`)
    await type('reset-password', 'a new passphrase')
    await type('reset-confirm', 'a new passphrase')
    await submit('reset-form')

    expect(byTestId('reset-invalid').textContent).toContain('Request a new one.')
  })

  it('treats a link without token as invalid', async () => {
    await mountApp('/reset-password')
    expect(byTestId('reset-invalid')).toBeTruthy()
  })
})

describe('email verification', () => {
  it('confirms the address from the link', async () => {
    resetMockAuth({ signedIn: false, user: { emailVerified: false } })
    await mountApp(`/verify-email?userId=${mockUser.id}&token=${mockCredentials.linkToken}`)

    expect(byTestId('verify-done')).toBeTruthy()
    expect(mockAuth.user.emailVerified).toBe(true)
    expect(byTestId('verify-continue').getAttribute('href')).toBe('/login')
  })

  it('offers a new link to a signed-in user when the token is invalid', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await mountApp(`/verify-email?userId=${mockUser.id}&token=expired`)

    expect(byTestId('verify-invalid').textContent).toContain('Send the link again')
  })

  it('continues to the calendar when signed in', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await mountApp(`/verify-email?userId=${mockUser.id}&token=${mockCredentials.linkToken}`)
    await settle()
    expect(byTestId('verify-continue').getAttribute('href')).toBe('/calendar')
  })
})
