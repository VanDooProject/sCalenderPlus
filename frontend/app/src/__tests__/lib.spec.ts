import { describe, expect, it } from 'vitest'
import { ApiError, NetworkError, call, parseRetryAfter } from '@/lib/apiError'
import { errorMessage, formatWait } from '@/lib/errorMessages'
import { safeNext } from '@/lib/redirect'
import { filterTimeZones } from '@/lib/timeZones'
import { createAppI18n } from '@/i18n'

const t = createAppI18n('en').global.t as (key: string, named?: Record<string, unknown>) => string

describe('ApiError', () => {
  it('uses the problem code and exposes field errors', () => {
    const error = new ApiError(400, {
      code: 'validation_failed',
      title: 'Validation failed',
      errors: { email: ['Required.'], 'items[0].name': ['Too long.'], bad: 'not a list' },
    })
    expect(error.code).toBe('validation_failed')
    expect(error.fieldErrors).toEqual({ email: ['Required.'], 'items[0].name': ['Too long.'] })
  })

  it.each([
    [401, 'unauthenticated'],
    [404, 'not_found'],
    [429, 'rate_limited'],
    [502, 'service_unavailable'],
    [500, 'internal_error'],
    [418, 'bad_request'],
  ] as const)('derives a code from status %i without a problem body', (status, code) => {
    expect(new ApiError(status, '<html>').code).toBe(code)
  })

  it('ignores unknown codes from the body', () => {
    expect(new ApiError(409, { code: 'something_new' }).code).toBe('bad_request')
  })
})

describe('call', () => {
  it('returns data and response for success', async () => {
    const response = new Response('{}', { status: 200 })
    await expect(call(Promise.resolve({ data: { ok: true }, response }))).resolves.toEqual({
      data: { ok: true },
      response,
    })
  })

  it('throws ApiError with Retry-After for problems', async () => {
    const response = new Response('', { status: 429, headers: { 'Retry-After': '30' } })
    const error = await call(Promise.resolve({ error: { code: 'rate_limited' }, response })).catch(
      (e: unknown) => e,
    )
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).retryAfter).toBe(30)
  })

  it('throws NetworkError when there is no response', async () => {
    await expect(call(Promise.reject(new TypeError('Failed to fetch')))).rejects.toBeInstanceOf(
      NetworkError,
    )
  })
})

describe('parseRetryAfter', () => {
  it('reads seconds and HTTP dates', () => {
    expect(parseRetryAfter('120')).toBe(120)
    expect(parseRetryAfter('Wed, 21 Oct 2026 07:28:30 GMT', Date.UTC(2026, 9, 21, 7, 28, 0))).toBe(
      30,
    )
    expect(parseRetryAfter(null)).toBeNull()
    expect(parseRetryAfter('soon')).toBeNull()
  })
})

describe('errorMessage', () => {
  it('translates the problem code, not the api title', () => {
    expect(
      errorMessage(t, new ApiError(401, { code: 'invalid_credentials', title: 'Invalid' })),
    ).toBe('Email address or password is incorrect.')
  })

  it('names the wait of a rate limit', () => {
    expect(errorMessage(t, new ApiError(429, { code: 'rate_limited' }, 42))).toBe(
      'Too many attempts. Please try again in 42 seconds.',
    )
    expect(errorMessage(t, new ApiError(429, { code: 'rate_limited' }))).toBe(
      'Too many attempts. Please wait a moment and try again.',
    )
  })

  it('formats long waits in minutes and hours', () => {
    expect(formatWait(1, 'en')).toBe('1 second')
    expect(formatWait(3600, 'en')).toBe('60 minutes')
    expect(formatWait(3 * 3600, 'de')).toBe('3 Stunden')
  })

  it('covers network and unknown errors', () => {
    expect(errorMessage(t, new NetworkError(null))).toMatch(/No connection/)
    expect(errorMessage(t, new Error('boom'))).toBe('Something went wrong. Please try again.')
  })
})

describe('safeNext', () => {
  it.each([
    ['/calendar', '/calendar'],
    ['/invite?token=abc', '/invite?token=abc'],
    [['/groups', '/x'], '/groups'],
  ])('accepts in-app path %j', (value, expected) => {
    expect(safeNext(value)).toBe(expected)
  })

  it.each([undefined, null, '', 'https://evil.test', '//evil.test', '/\\evil.test', 'calendar'])(
    'rejects %j',
    (value) => {
      expect(safeNext(value)).toBeUndefined()
    },
  )
})

describe('filterTimeZones', () => {
  const zones = ['America/New_York', 'Europe/Berlin', 'Europe/Vienna', 'UTC']

  it('matches words of the id, case-insensitively', () => {
    expect(filterTimeZones(zones, 'new york')).toEqual(['America/New_York'])
    expect(filterTimeZones(zones, 'EUROPE')).toEqual(['Europe/Berlin', 'Europe/Vienna'])
    expect(filterTimeZones(zones, '')).toEqual(zones)
    expect(filterTimeZones(zones, 'europe', 1)).toEqual(['Europe/Berlin'])
  })
})
