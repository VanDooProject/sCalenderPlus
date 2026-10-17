import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import type { ErrorCode } from '../index'
import { mockAuth, mockProblem } from './auth'
import { mockCalendar, mockCalendars } from './calendars'

type FeedToken = components['schemas']['FeedTokenResponse']
type IssuedFeedToken = components['schemas']['IssuedFeedTokenResponse']
type ShareLink = components['schemas']['ShareLinkResponse']

/** Mutable feed links and share links of the mocks (reset per test with {@link resetMockFeeds}). */
export interface MockFeedsState {
  tokens: FeedToken[]
  shareLinks: ShareLink[]
  /** Counter for generated ids and secrets. */
  sequence: number
}

const base = 'http://localhost:5173'
const scopes = ['calendar', 'aggregate', 'shared_with_me']
const labelModes = ['prefix_emoji', 'prefix_emoji_both', 'suffix_text', 'description_only', 'none']

/** The mock user's aggregate feed link (the recommended default). */
export const mockFeedToken: FeedToken = {
  id: '0192f2c4-0000-7000-8000-000000000701',
  scope: 'aggregate',
  calendarId: null,
  name: 'iPhone',
  labelMode: 'prefix_emoji',
  includePastDays: 90,
  needsNewLink: false,
  lastUsedAt: '2026-10-16T07:45:00Z',
  rotatedAt: null,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  etag: '"feed-0701-1"',
}

/** A calendar feed link of {@link mockCalendar} whose secret a password reset replaced. */
export const mockResetFeedToken: FeedToken = {
  ...mockFeedToken,
  id: '0192f2c4-0000-7000-8000-000000000702',
  scope: 'calendar',
  calendarId: mockCalendar.id,
  name: null,
  labelMode: 'suffix_text',
  needsNewLink: true,
  lastUsedAt: null,
  rotatedAt: '2026-10-10T12:00:00Z',
  etag: '"feed-0702-1"',
}

/** A free/busy share link of {@link mockCalendar} for the club website. */
export const mockShareLink: ShareLink = {
  id: '0192f2c4-0000-7000-8000-000000000711',
  calendarId: mockCalendar.id,
  level: 'free_busy',
  label: 'Club website',
  expiresAt: null,
  createdBy: '0192f2c4-0000-7000-8000-000000000001',
  createdAt: '2026-10-01T08:00:00Z',
  lastUsedAt: '2026-10-16T22:10:00Z',
}

function seed(): MockFeedsState {
  return {
    tokens: [{ ...mockFeedToken }, { ...mockResetFeedToken }],
    shareLinks: [{ ...mockShareLink }],
    sequence: 0,
  }
}

export const mockFeeds: MockFeedsState = seed()

export function resetMockFeeds(): void {
  Object.assign(mockFeeds, seed())
}

/** A deterministic 43-character secret (base64url, like the api's). */
function secret(n: number): string {
  return `mockSecret${String(n).padStart(4, '0')}`.padEnd(43, 'x')
}

function nextId(): string {
  mockFeeds.sequence += 1
  return `0192f2c4-0000-7000-8000-${String(800 + mockFeeds.sequence).padStart(12, '0')}`
}

function issue(token: FeedToken): IssuedFeedToken {
  mockFeeds.sequence += 1
  const value = secret(mockFeeds.sequence)
  const url = `${base}/ical/v1/${value}.ics`
  return { feedToken: token, token: value, url, webcalUrl: url.replace(/^https?:/, 'webcal:') }
}

function touch(token: FeedToken): void {
  const version = Number(token.etag.match(/-(\d+)"$/)?.[1] ?? '1') + 1
  token.etag = `"feed-${token.id.slice(-4)}-${version}"`
  token.updatedAt = new Date().toISOString()
}

/**
 * Feed links (`/api/v1/feed-tokens`) and calendar share links (`/api/v1/calendars/{id}/share-links`,
 * `/api/v1/share-links/{id}`) like the api: secrets only in create/rotate responses, If-Match on PATCH,
 * share links for calendar managers with a verified email, not on frozen calendars (the mock user
 * manages {@link mockCalendar} and their personal calendar).
 */
export function feedHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  const problem = (code: ErrorCode, status: number, instance: string) =>
    new Response(JSON.stringify(mockProblem(code, status, instance)), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    })
  const find = (id: string) => mockFeeds.tokens.find((t) => t.id === id)
  const manages = (calendarId: string) => {
    const calendar = mockCalendars.calendars.find((c) => c.id === calendarId)
    return calendar ? ['manage', 'owner'].includes(calendar.myLevel) : undefined
  }

  return [
    http.get('/api/v1/feed-tokens', ({ response }) =>
      response(200).json({ items: mockFeeds.tokens }),
    ),

    http.post('/api/v1/feed-tokens', async ({ request, response }) => {
      const body = await request.json()
      const instance = '/api/v1/feed-tokens'
      if (!scopes.includes(body.scope)) return problem('validation_failed', 400, instance)
      if ((body.scope === 'calendar') !== Boolean(body.calendarId)) {
        return problem('validation_failed', 400, instance)
      }
      if (body.calendarId && !mockCalendars.calendars.some((c) => c.id === body.calendarId)) {
        return problem('not_found', 404, instance)
      }
      if (body.labelMode && !labelModes.includes(body.labelMode)) {
        return problem('validation_failed', 400, instance)
      }
      const now = new Date().toISOString()
      const id = nextId()
      const token: FeedToken = {
        id,
        scope: body.scope,
        calendarId: body.calendarId ?? null,
        name: body.name?.trim() || null,
        labelMode: body.labelMode ?? 'prefix_emoji',
        includePastDays: body.includePastDays ?? 90,
        needsNewLink: false,
        lastUsedAt: null,
        rotatedAt: null,
        createdAt: now,
        updatedAt: now,
        etag: `"feed-${id.slice(-4)}-1"`,
      }
      mockFeeds.tokens.push(token)
      return response(201).json(issue(token))
    }),

    http.post('/api/v1/feed-tokens/rotate-all', ({ response }) => {
      const now = new Date().toISOString()
      const items = mockFeeds.tokens.map((t) => {
        Object.assign(t, { rotatedAt: now, needsNewLink: false })
        touch(t)
        return issue(t)
      })
      return response(200).json({ items })
    }),

    http.get('/api/v1/feed-tokens/{id}', ({ params, response }) => {
      const token = find(params.id)
      if (!token) return problem('not_found', 404, `/api/v1/feed-tokens/${params.id}`)
      return response(200).json(token)
    }),

    http.patch('/api/v1/feed-tokens/{id}', async ({ params, request, response }) => {
      const instance = `/api/v1/feed-tokens/${params.id}`
      const token = find(params.id)
      if (!token) return problem('not_found', 404, instance)
      const ifMatch = request.headers.get('If-Match')
      if (!ifMatch) return problem('precondition_required', 428, instance)
      if (ifMatch !== '*' && ifMatch !== token.etag)
        return problem('precondition_failed', 412, instance)
      const patch = await request.json()
      if (patch.labelMode != null && !labelModes.includes(patch.labelMode)) {
        return problem('validation_failed', 400, instance)
      }
      if (patch.name != null) token.name = patch.name.trim() || null
      if (patch.labelMode != null) token.labelMode = patch.labelMode
      if (patch.includePastDays != null) token.includePastDays = patch.includePastDays
      touch(token)
      return response(200).json(token)
    }),

    http.delete('/api/v1/feed-tokens/{id}', ({ params, response }) => {
      const index = mockFeeds.tokens.findIndex((t) => t.id === params.id)
      if (index < 0) return problem('not_found', 404, `/api/v1/feed-tokens/${params.id}`)
      mockFeeds.tokens.splice(index, 1)
      return response(204).empty()
    }),

    http.post('/api/v1/feed-tokens/{id}/rotate', ({ params, response }) => {
      const token = find(params.id)
      if (!token) return problem('not_found', 404, `/api/v1/feed-tokens/${params.id}/rotate`)
      Object.assign(token, { rotatedAt: new Date().toISOString(), needsNewLink: false })
      touch(token)
      return response(200).json(issue(token))
    }),

    http.get('/api/v1/calendars/{id}/share-links', ({ params, response }) => {
      const instance = `/api/v1/calendars/${params.id}/share-links`
      const allowed = manages(params.id)
      if (allowed === undefined) return problem('not_found', 404, instance)
      if (!allowed) return problem('insufficient_permission', 403, instance)
      return response(200).json({
        items: mockFeeds.shareLinks.filter((l) => l.calendarId === params.id),
      })
    }),

    http.post('/api/v1/calendars/{id}/share-links', async ({ params, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}/share-links`
      const allowed = manages(params.id)
      if (allowed === undefined) return problem('not_found', 404, instance)
      if (!allowed) return problem('insufficient_permission', 403, instance)
      if (!mockAuth.user.emailVerified) return problem('email_not_verified', 403, instance)
      if (mockCalendars.calendars.find((c) => c.id === params.id)?.frozen) {
        return problem('calendar_frozen', 409, instance)
      }
      const body = await request.json()
      if (!['free_busy', 'read'].includes(body.level))
        return problem('validation_failed', 400, instance)
      const now = new Date()
      const link: ShareLink = {
        id: nextId(),
        calendarId: params.id,
        level: body.level,
        label: body.label?.trim() || null,
        expiresAt: body.expiresInDays
          ? new Date(now.getTime() + body.expiresInDays * 86_400_000).toISOString()
          : null,
        createdBy: '0192f2c4-0000-7000-8000-000000000001',
        createdAt: now.toISOString(),
        lastUsedAt: null,
      }
      mockFeeds.shareLinks.push(link)
      mockFeeds.sequence += 1
      const value = secret(mockFeeds.sequence)
      const url = `${base}/ical/v1/s/${value}.ics`
      return response(201).json({
        shareLink: link,
        token: value,
        url,
        webcalUrl: url.replace(/^https?:/, 'webcal:'),
      })
    }),

    http.delete('/api/v1/share-links/{id}', ({ params, response }) => {
      const instance = `/api/v1/share-links/${params.id}`
      const link = mockFeeds.shareLinks.find((l) => l.id === params.id)
      if (!link) return problem('not_found', 404, instance)
      if (!manages(link.calendarId)) return problem('insufficient_permission', 403, instance)
      mockFeeds.shareLinks.splice(mockFeeds.shareLinks.indexOf(link), 1)
      return response(204).empty()
    }),
  ]
}
