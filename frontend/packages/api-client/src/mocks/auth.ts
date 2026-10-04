import { http as mswHttp, HttpResponse } from 'msw'
import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'

type MeResponse = components['schemas']['MeResponse']
type ErrorCode = components['schemas']['ErrorCode']
type ProblemDetails = components['schemas']['ProblemDetails']

/** The signed-in user of mock mode. */
export const mockUser: MeResponse = {
  id: '0192f2c4-0000-7000-8000-000000000001',
  email: 'mia@example.test',
  emailVerified: true,
  displayName: 'Mia',
  locale: 'en',
  timeZone: 'Europe/Berlin',
  weekStart: 'monday',
  twoFactorEnabled: false,
  createdAt: '2026-10-01T08:00:00Z',
}

/**
 * Inputs that steer the stateful auth mocks (login, 2FA, email links). Tests and the mocked e2e
 * suite use these instead of magic strings.
 */
export const mockCredentials = {
  /** The only password the mocks accept (login, re-authentication). */
  password: 'correct-horse-42',
  /** Logging in with this address asks for a second factor. */
  twoFactorEmail: 'two-factor@example.test',
  /** Logging in with this address answers `429 rate_limited` with `Retry-After`. */
  rateLimitedEmail: 'limited@example.test',
  retryAfterSeconds: 42,
  /** The current authenticator code. */
  totpCode: '123456',
  /** A valid recovery code. */
  recoveryCode: 'ABCDE-12345',
  /** Token of valid `/verify-email` and `/reset-password` links; any other is `token_invalid`. */
  linkToken: 'mock-link-token',
  /** Shared key returned by 2FA setup. */
  sharedKey: 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP',
} as const

export interface MockAuthState {
  signedIn: boolean
  /** A password login waits for its second factor (`__Host-scal-2fa`). */
  pendingTwoFactor: boolean
  user: MeResponse
  /** Bumped on each profile change; the `ETag` of `/me` is derived from it. */
  version: number
  recoveryCodesLeft: number
}

function initialState(): MockAuthState {
  return {
    signedIn: true,
    pendingTwoFactor: false,
    user: { ...mockUser },
    version: 1,
    recoveryCodesLeft: 0,
  }
}

/**
 * Mutable session of the auth mocks. Mock mode starts signed in; tests call
 * {@link resetMockAuth} (e.g. `{ signedIn: false }`) to pick the starting point.
 */
export const mockAuth: MockAuthState = initialState()

export function resetMockAuth(
  overrides: Partial<Omit<MockAuthState, 'user'>> & { user?: Partial<MeResponse> } = {},
): void {
  const { user, ...rest } = overrides
  const base = initialState()
  Object.assign(mockAuth, base, rest, { user: { ...base.user, ...user } })
}

const titles: Partial<Record<ErrorCode, string>> = {
  unauthenticated: 'Unauthenticated',
  invalid_credentials: 'Invalid credentials',
  rate_limited: 'Too many requests',
  token_invalid: 'Invalid token',
  validation_failed: 'Validation failed',
  precondition_failed: 'Precondition failed',
  precondition_required: 'Precondition required',
  reauthentication_failed: 'Reauthentication failed',
  email_not_verified: 'Email not verified',
  conflict: 'Conflict',
  time_zone_invalid: 'Unknown time zone',
}

/** A problem body like the api's (`type`, `title`, `status`, `code`, …). */
export function mockProblem(
  code: ErrorCode,
  status: number,
  instance: string,
  extras: Partial<ProblemDetails> = {},
): ProblemDetails {
  return {
    type: `https://scalenderplus.app/problems/${code.replaceAll('_', '-')}`,
    title: titles[code] ?? code,
    status,
    code,
    instance,
    traceId: '00-mock',
    ...extras,
  }
}

const etag = () => `"me-${mockAuth.version}"`

function recoveryCodes(): string[] {
  return Array.from({ length: 10 }, (_, i) => `MOCK${i}-${String(10000 + i * 1111).slice(0, 5)}`)
}

function knownTimeZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat('en', { timeZone: zone })
    return zone.includes('/') || zone === 'UTC'
  } catch {
    return false
  }
}

/**
 * Answers `401 unauthenticated` for every protected `/api/v1` request while signed out, like the
 * api (`/auth/*` handle the session themselves); otherwise falls through to the next handler.
 */
export const sessionGuard = mswHttp.all('*/api/v1/*', ({ request }) => {
  const path = new URL(request.url).pathname
  // Anonymous endpoints: the auth flows and the invite preview (the token is the credential).
  if (mockAuth.signedIn || path.startsWith('/api/v1/auth/') || path === '/api/v1/invites/preview') {
    return undefined
  }
  return HttpResponse.json(
    mockProblem('unauthenticated', 401, path, { detail: 'Sign in first.' }),
    { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
  )
})

/** Stateful handlers for `/auth/*`, `/me` and `/me/two-factor*`. */
export function authHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  const unauthenticated = (instance: string) =>
    mockProblem('unauthenticated', 401, instance, { detail: 'Sign in first.' })

  return [
    http.post('/api/v1/auth/register', async ({ request, response }) => {
      const body = await request.json()
      if (body.password.length < 10) {
        return response('default').json(
          mockProblem('validation_failed', 400, '/api/v1/auth/register', {
            errors: { password: ['Passwords must be at least 10 characters.'] },
          }),
          { status: 400 },
        )
      }
      return response(202).empty()
    }),

    http.post('/api/v1/auth/login', async ({ request, response }) => {
      const body = await request.json()
      const instance = '/api/v1/auth/login'
      if (body.email === mockCredentials.rateLimitedEmail) {
        return response('default').json(mockProblem('rate_limited', 429, instance), {
          status: 429,
          headers: { 'Retry-After': String(mockCredentials.retryAfterSeconds) },
        })
      }
      if (body.password !== mockCredentials.password) {
        return response('default').json(
          mockProblem('invalid_credentials', 401, instance, {
            detail: 'Email address or password is incorrect.',
          }),
          { status: 401 },
        )
      }
      mockAuth.user = { ...mockAuth.user, email: body.email }
      if (body.email === mockCredentials.twoFactorEmail || mockAuth.user.twoFactorEnabled) {
        mockAuth.pendingTwoFactor = true
        mockAuth.signedIn = false
        return response(200).json({ twoFactorRequired: true, user: null })
      }
      mockAuth.signedIn = true
      return response(200).json({ twoFactorRequired: false, user: mockAuth.user })
    }),

    http.post('/api/v1/auth/login/2fa', async ({ request, response }) => {
      const instance = '/api/v1/auth/login/2fa'
      if (!mockAuth.pendingTwoFactor) {
        return response('default').json(unauthenticated(instance), { status: 401 })
      }
      const body = await request.json()
      const ok =
        body.code === mockCredentials.totpCode ||
        body.recoveryCode?.toUpperCase() === mockCredentials.recoveryCode
      if (!ok) {
        return response('default').json(
          mockProblem('invalid_credentials', 401, instance, {
            detail: 'The code is incorrect.',
          }),
          { status: 401 },
        )
      }
      mockAuth.pendingTwoFactor = false
      mockAuth.signedIn = true
      mockAuth.user = { ...mockAuth.user, twoFactorEnabled: true }
      return response(200).json({ twoFactorRequired: false, user: mockAuth.user })
    }),

    http.post('/api/v1/auth/logout', ({ response }) => {
      if (!mockAuth.signedIn) {
        return response('default').json(unauthenticated('/api/v1/auth/logout'), { status: 401 })
      }
      mockAuth.signedIn = false
      return response(204).empty()
    }),

    http.post('/api/v1/auth/confirm-email', async ({ request, response }) => {
      const body = await request.json()
      if (body.token !== mockCredentials.linkToken) {
        return response('default').json(
          mockProblem('token_invalid', 400, '/api/v1/auth/confirm-email'),
          { status: 400 },
        )
      }
      mockAuth.user = { ...mockAuth.user, emailVerified: true }
      mockAuth.version++
      return response(204).empty()
    }),

    http.post('/api/v1/auth/confirm-email/resend', ({ response }) =>
      mockAuth.signedIn
        ? response(204).empty()
        : response('default').json(unauthenticated('/api/v1/auth/confirm-email/resend'), {
            status: 401,
          }),
    ),

    http.post('/api/v1/auth/forgot-password', ({ response }) => response(202).empty()),

    http.post('/api/v1/auth/reset-password', async ({ request, response }) => {
      const body = await request.json()
      const instance = '/api/v1/auth/reset-password'
      if (body.token !== mockCredentials.linkToken) {
        return response('default').json(mockProblem('token_invalid', 400, instance), {
          status: 400,
        })
      }
      if (body.newPassword.length < 10) {
        return response('default').json(
          mockProblem('validation_failed', 400, instance, {
            errors: { newPassword: ['Passwords must be at least 10 characters.'] },
          }),
          { status: 400 },
        )
      }
      mockAuth.signedIn = false
      return response(204).empty()
    }),

    http.get('/api/v1/me', ({ response }) =>
      mockAuth.signedIn
        ? response(200).json(mockAuth.user, { headers: { ETag: etag() } })
        : response('default').json(unauthenticated('/api/v1/me'), { status: 401 }),
    ),

    http.patch('/api/v1/me', async ({ request, response }) => {
      const instance = '/api/v1/me'
      if (!mockAuth.signedIn) {
        return response('default').json(unauthenticated(instance), { status: 401 })
      }
      const ifMatch = request.headers.get('If-Match')
      if (!ifMatch) {
        return response('default').json(mockProblem('precondition_required', 428, instance), {
          status: 428,
        })
      }
      if (ifMatch !== '*' && ifMatch !== etag()) {
        return response('default').json(mockProblem('precondition_failed', 412, instance), {
          status: 412,
        })
      }
      const patch = (await request.json()) as components['schemas']['UpdateProfileRequest']
      if (typeof patch.displayName === 'string' && patch.displayName.trim() === '') {
        return response('default').json(
          mockProblem('validation_failed', 400, instance, {
            errors: { displayName: ['The display name must not be empty.'] },
          }),
          { status: 400 },
        )
      }
      if (typeof patch.timeZone === 'string' && !knownTimeZone(patch.timeZone)) {
        return response('default').json(
          mockProblem('time_zone_invalid', 422, instance, {
            errors: { timeZone: ['Use an IANA time zone id such as Europe/Berlin.'] },
          }),
          { status: 422 },
        )
      }
      const updated = { ...mockAuth.user }
      for (const key of ['displayName', 'locale', 'timeZone', 'weekStart'] as const) {
        const value = patch[key]
        if (typeof value === 'string') updated[key] = key === 'displayName' ? value.trim() : value
      }
      mockAuth.user = updated
      mockAuth.version++
      return response(200).json(updated, { headers: { ETag: etag() } })
    }),

    http.get('/api/v1/me/two-factor', ({ response }) =>
      mockAuth.signedIn
        ? response(200).json({
            enabled: mockAuth.user.twoFactorEnabled,
            recoveryCodesLeft: mockAuth.user.twoFactorEnabled ? mockAuth.recoveryCodesLeft : 0,
          })
        : response('default').json(unauthenticated('/api/v1/me/two-factor'), { status: 401 }),
    ),

    http.post('/api/v1/me/two-factor/setup', ({ response }) => {
      const instance = '/api/v1/me/two-factor/setup'
      if (!mockAuth.user.emailVerified) {
        return response('default').json(mockProblem('email_not_verified', 403, instance), {
          status: 403,
        })
      }
      if (mockAuth.user.twoFactorEnabled) {
        return response('default').json(mockProblem('conflict', 409, instance), { status: 409 })
      }
      const label = encodeURIComponent(`sCalenderPlus:${mockAuth.user.email}`)
      return response(200).json({
        sharedKey: mockCredentials.sharedKey,
        authenticatorUri: `otpauth://totp/${label}?secret=${mockCredentials.sharedKey}&issuer=sCalenderPlus&digits=6`,
      })
    }),

    http.post('/api/v1/me/two-factor/enable', async ({ request, response }) => {
      const instance = '/api/v1/me/two-factor/enable'
      const body = await request.json()
      if (body.password !== mockCredentials.password) {
        return response('default').json(mockProblem('reauthentication_failed', 403, instance), {
          status: 403,
        })
      }
      if (body.code.replaceAll(' ', '') !== mockCredentials.totpCode) {
        return response('default').json(
          mockProblem('validation_failed', 400, instance, {
            errors: { code: ['The code is invalid.'] },
          }),
          { status: 400 },
        )
      }
      mockAuth.user = { ...mockAuth.user, twoFactorEnabled: true }
      mockAuth.recoveryCodesLeft = 10
      mockAuth.version++
      return response(200).json({ recoveryCodes: recoveryCodes() })
    }),

    http.post('/api/v1/me/two-factor/disable', async ({ request, response }) => {
      const body = await request.json()
      if (body.password !== mockCredentials.password && body.code !== mockCredentials.totpCode) {
        return response('default').json(
          mockProblem('reauthentication_failed', 403, '/api/v1/me/two-factor/disable'),
          { status: 403 },
        )
      }
      mockAuth.user = { ...mockAuth.user, twoFactorEnabled: false }
      mockAuth.recoveryCodesLeft = 0
      mockAuth.version++
      return response(204).empty()
    }),

    http.post('/api/v1/me/two-factor/recovery-codes', async ({ request, response }) => {
      const instance = '/api/v1/me/two-factor/recovery-codes'
      if (!mockAuth.user.twoFactorEnabled) {
        return response('default').json(mockProblem('conflict', 409, instance), { status: 409 })
      }
      const body = await request.json()
      if (body.password !== mockCredentials.password && body.code !== mockCredentials.totpCode) {
        return response('default').json(mockProblem('reauthentication_failed', 403, instance), {
          status: 403,
        })
      }
      mockAuth.recoveryCodesLeft = 10
      return response(200).json({ recoveryCodes: recoveryCodes() })
    }),
  ]
}
