import { afterEach, describe, expect, it, vi } from 'vitest'
import { Temporal } from 'temporal-polyfill'
import {
  http,
  mockAuth,
  mockCalendar,
  mockCalendars,
  mockCredentials,
  mockFeeds,
  mockFeedToken,
  mockResetFeedToken,
  mockShareLink,
  mockUser,
  resetMockAuth,
} from '@scalenderplus/api-client/mocks'
import type { EventResponse } from '@scalenderplus/api-client'
import { eventLinkDate } from '@/composables/eventLink'
import {
  byTestId,
  click,
  mountApp,
  queryTestId,
  resetDom,
  settle,
  submit,
  type,
  until,
} from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(() => {
  server.events.removeAllListeners()
  resetDom()
})

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''

interface Recorded {
  method: string
  path: string
  body: unknown
}

function recordMutations() {
  const requests: Recorded[] = []
  server.events.on('request:start', async ({ request }) => {
    if (request.method === 'GET') return
    const url = new URL(request.url)
    const raw = await request.clone().text()
    requests.push({
      method: request.method,
      path: url.pathname,
      body: raw ? JSON.parse(raw) : null,
    })
  })
  return requests
}

/** Picks an option of a `UiSelect` by its label. */
async function choose(testid: string, label: string) {
  byTestId(testid).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
  await settle()
  const option = [...document.querySelectorAll<HTMLElement>('[role="option"]')].find(
    (o) => text(o) === label,
  )
  if (!option) throw new Error(`No option ${label}`)
  option.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
  await settle()
}

/** Whether a secret of the mocks (`mockSecret…`) appears anywhere in the page. */
const showsSecret = () => document.body.innerHTML.includes('mockSecret')

async function feedsPage() {
  const app = await mountApp('/settings/feeds')
  await until(() => queryTestId('feeds-list') !== null)
  return app
}

describe('feed links page', () => {
  it('lists the links without secrets and asks for new addresses after a password reset', async () => {
    await feedsPage()
    expect(text(byTestId(`feed-${mockFeedToken.id}`))).toContain('iPhone')
    expect(text(byTestId(`feed-${mockFeedToken.id}`))).toContain('All my calendars')
    expect(text(byTestId(`feed-${mockResetFeedToken.id}`))).toContain(
      `Calendar: ${mockCalendar.name}`,
    )
    expect(text(byTestId('feeds-needs-new'))).toContain('A feed link stopped working')
    expect(queryTestId(`feed-rotate-${mockResetFeedToken.id}`)).toBeNull()
    expect(showsSecret()).toBe(false)

    const requests = recordMutations()
    await click(`feed-renew-${mockResetFeedToken.id}`)
    expect(requests).toEqual([
      { method: 'POST', path: `/api/v1/feed-tokens/${mockResetFeedToken.id}/rotate`, body: null },
    ])
    const url = (byTestId('revealed-feed-url') as HTMLInputElement).value
    expect(url).toMatch(/^http:\/\/localhost:5173\/ical\/v1\/mockSecret\w+\.ics$/)
    expect(byTestId('revealed-feed-webcal').getAttribute('href')).toBe(
      url.replace(/^http:/, 'webcal:'),
    )
    expect(queryTestId('qr-code')).not.toBeNull()

    await click('feed-reveal-done')
    await until(() => queryTestId('feeds-needs-new') === null)
    expect(showsSecret()).toBe(false)
    // The first test of the file also loads the lazy route chunks.
  }, 15_000)

  it('creates a link and shows its address once', async () => {
    await feedsPage()
    const requests = recordMutations()
    await click('feed-create')
    await choose('feed-scope', 'One calendar')
    await submit('create-feed-form')
    expect(text(byTestId('create-feed-dialog'))).toContain('Choose a calendar first.')
    expect(requests).toEqual([])

    await choose('feed-calendar', mockCalendar.name)
    await choose('feed-label-mode', '🔒 read-only and ✏️ editable')
    await type('feed-name', '  Laptop ')
    await submit('create-feed-form')
    expect(requests).toEqual([
      {
        method: 'POST',
        path: '/api/v1/feed-tokens',
        body: {
          scope: 'calendar',
          calendarId: mockCalendar.id,
          name: 'Laptop',
          labelMode: 'prefix_emoji_both',
          includePastDays: 90,
        },
      },
    ])
    expect((byTestId('created-feed-url') as HTMLInputElement).value).toContain('/ical/v1/')

    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    await click('created-feed-copy')
    expect(writeText).toHaveBeenCalledWith((byTestId('created-feed-url') as HTMLInputElement).value)

    await click('created-feed-done')
    await until(() => text(byTestId('feeds-list')).includes('Laptop'))
    expect(showsSecret()).toBe(false)
  })

  it('revokes a link after confirming', async () => {
    await feedsPage()
    const requests = recordMutations()
    await click(`feed-revoke-${mockFeedToken.id}`)
    expect(text(byTestId('feed-confirm-dialog'))).toContain('Revoke iPhone?')
    expect(text(byTestId('feed-confirm-dialog'))).toContain('Nobody can recall them sooner')
    await click('feed-confirm-submit')
    expect(requests).toEqual([
      { method: 'DELETE', path: `/api/v1/feed-tokens/${mockFeedToken.id}`, body: null },
    ])
    await until(() => queryTestId(`feed-${mockFeedToken.id}`) === null)
    expect(mockFeeds.tokens.map((t) => t.id)).toEqual([mockResetFeedToken.id])
  })

  it('replaces all addresses and lists each new one', async () => {
    await feedsPage()
    await click('feed-rotate-all')
    await click('feed-confirm-submit')
    await until(() => queryTestId(`revealed-feed-${mockFeedToken.id}`) !== null)
    expect(queryTestId(`revealed-feed-${mockResetFeedToken.id}`)).not.toBeNull()
    expect(text(byTestId('feed-reveal-dialog'))).toContain('New addresses')
  })

  it('explains the limit of links', async () => {
    server.use(
      http.post('/api/v1/feed-tokens', ({ response }) =>
        response('default').json(
          { type: 'about:blank', title: 'Conflict', status: 409, code: 'conflict' },
          { status: 409 },
        ),
      ),
    )
    await feedsPage()
    await click('feed-create')
    await submit('create-feed-form')
    expect(text(byTestId('create-feed-dialog'))).toContain('maximum number of feed links')
  })
})

describe('subscribe to one calendar', () => {
  it('creates a calendar feed when there is none, and offers a new address for an existing one', async () => {
    mockFeeds.tokens = [{ ...mockFeedToken }]
    await mountApp(`/calendars/${mockCalendar.id}/settings`)
    await until(() => queryTestId('calendar-subscribe') !== null)
    const requests = recordMutations()
    await click('calendar-subscribe')
    await until(() => queryTestId('subscribe-create') !== null)
    await click('subscribe-create')
    expect(requests).toEqual([
      {
        method: 'POST',
        path: '/api/v1/feed-tokens',
        body: { scope: 'calendar', calendarId: mockCalendar.id },
      },
    ])
    expect((byTestId('subscribe-feed-url') as HTMLInputElement).value).toContain('/ical/v1/')
    await click('subscribe-done')

    await click('calendar-subscribe')
    await until(() => queryTestId('subscribe-existing') !== null)
    const created = mockFeeds.tokens.find((t) => t.calendarId === mockCalendar.id)!
    await click('subscribe-renew')
    expect(requests.at(-1)).toEqual({
      method: 'POST',
      path: `/api/v1/feed-tokens/${created.id}/rotate`,
      body: null,
    })
  })
})

describe('share links', () => {
  async function settings() {
    await mountApp(`/calendars/${mockCalendar.id}/settings`)
    await until(() => queryTestId('share-links-list') !== null)
  }

  it('lists, creates with expiry and revokes', async () => {
    await settings()
    expect(text(byTestId(`share-link-${mockShareLink.id}`))).toContain('Club website')
    expect(text(byTestId(`share-link-${mockShareLink.id}`))).toContain('Busy times only')
    const requests = recordMutations()

    await click('share-link-create')
    await choose('share-link-level', 'Events with details')
    await choose('share-link-expiry', 'After 30 days')
    await type('share-link-label', 'Parents')
    await submit('share-link-form')
    expect(requests).toEqual([
      {
        method: 'POST',
        path: `/api/v1/calendars/${mockCalendar.id}/share-links`,
        body: { level: 'read', label: 'Parents', expiresInDays: 30 },
      },
    ])
    expect((byTestId('created-share-link-url') as HTMLInputElement).value).toContain('/ical/v1/s/')
    await click('share-link-done')
    await until(() => text(byTestId('share-links-list')).includes('Parents'))
    expect(showsSecret()).toBe(false)

    await click(`share-link-revoke-${mockShareLink.id}`)
    await click('share-link-revoke-submit')
    expect(requests.at(-1)).toEqual({
      method: 'DELETE',
      path: `/api/v1/share-links/${mockShareLink.id}`,
      body: null,
    })
    await until(() => queryTestId(`share-link-${mockShareLink.id}`) === null)
  })

  it('asks unverified users to confirm their email first', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await settings()
    expect(queryTestId('share-links-verify')).not.toBeNull()
    expect(byTestId('share-link-create').hasAttribute('disabled')).toBe(true)
  })

  it('is only offered to managers', async () => {
    const readOnly = mockCalendars.calendars.find((c) => c.myLevel === 'read')!
    await mountApp(`/calendars/${readOnly.id}/settings`)
    await until(() => queryTestId('calendar-subscribe') !== null)
    expect(queryTestId('share-links')).toBeNull()
  })
})

describe('event links', () => {
  it('/e/{id} opens the calendar at the event with its details', async () => {
    const event = mockCalendars.events.find((e) => e.title === 'Lunch with Max')!
    const { router } = await mountApp(`/e/${event.id}`)
    await until(() => queryTestId('event-details') !== null)
    expect(router.currentRoute.value.name).toBe('calendar')
    const date = Temporal.Instant.from(event.start.utc!)
      .toZonedDateTimeISO(mockAuth.user.timeZone)
      .toPlainDate()
      .toString()
    expect(router.currentRoute.value.query).toEqual({ date })
    expect(text(byTestId('event-details'))).toContain('Lunch with Max')
  })

  it('/e/{id}/edit opens the edit dialog when the event is editable', async () => {
    const event = mockCalendars.events.find((e) => e.title === 'Lunch with Max')!
    await mountApp(`/e/${event.id}/edit`)
    await until(() => document.querySelector('[role="dialog"] input') !== null)
    expect(queryTestId('event-details')).toBeNull()
    expect(text(document.querySelector('[role="dialog"]'))).toContain('Edit event')
  })

  it('/e/{id}/edit of a read-only event shows its details', async () => {
    const event = mockCalendars.events.find((e) => e.title === 'Board meeting')!
    await mountApp(`/e/${event.id}/edit`)
    await until(() => queryTestId('event-details') !== null)
  })

  it('explains an event that is gone or no longer visible', async () => {
    const { router } = await mountApp('/e/0192f2c4-0000-7000-8000-00000000dead')
    await until(() => queryTestId('event-link-gone') !== null)
    expect(router.currentRoute.value.name).toBe('event-link')
    expect(text(byTestId('event-link-gone'))).toContain('Event not available')
  })

  it('signed-out visitors sign in first and come back', async () => {
    resetMockAuth({ signedIn: false })
    const { router } = await mountApp(`/e/${mockCalendars.events[0]!.id}`)
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe(`/e/${mockCalendars.events[0]!.id}`)
  })

  it('picks the day to open', () => {
    const base = { id: 'x', calendarId: 'c', myLevel: 'read', allDay: false } as const
    const timed = {
      ...base,
      start: {
        dateTime: '2026-03-01T00:30:00',
        timeZone: 'Europe/Berlin',
        utc: '2026-02-28T23:30:00Z',
      },
      end: {
        dateTime: '2026-03-01T01:30:00',
        timeZone: 'Europe/Berlin',
        utc: '2026-03-01T00:30:00Z',
      },
    } as unknown as EventResponse
    expect(eventLinkDate(timed, 'Europe/Berlin').toString()).toBe('2026-03-01')
    expect(eventLinkDate(timed, 'America/New_York').toString()).toBe('2026-02-28')
    expect(eventLinkDate(timed, 'Not/AZone').toString()).toBe('2026-02-28')
    const allDay = {
      ...base,
      allDay: true,
      start: { date: '2027-01-05' },
      end: { date: '2027-01-06' },
    }
    expect(eventLinkDate(allDay as unknown as EventResponse, 'Pacific/Kiritimati').toString()).toBe(
      '2027-01-05',
    )
    const series = { ...timed, recurrence: { rrule: 'FREQ=WEEKLY' } } as unknown as EventResponse
    expect(eventLinkDate(series, 'UTC').toString()).toBe(
      Temporal.Now.plainDateISO('UTC').toString(),
    )
  })
})

describe('password reset', () => {
  it('can rotate the feed links too', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp(`/reset-password?userId=${mockUser.id}&token=${mockCredentials.linkToken}`)
    const requests = recordMutations()
    await type('reset-password', 'a new passphrase')
    await type('reset-confirm', 'a new passphrase')
    byTestId('reset-rotate-feeds').querySelector<HTMLElement>('[role="checkbox"]')!.click()
    await settle()
    await submit('reset-form')
    expect(requests).toEqual([
      {
        method: 'POST',
        path: '/api/v1/auth/reset-password',
        body: {
          userId: mockUser.id,
          token: mockCredentials.linkToken,
          newPassword: 'a new passphrase',
          rotateFeedTokens: true,
        },
      },
    ])
    expect(queryTestId('reset-feeds-rotated')).not.toBeNull()
  })

  it('leaves the feed links alone by default', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp(`/reset-password?userId=${mockUser.id}&token=${mockCredentials.linkToken}`)
    const requests = recordMutations()
    await type('reset-password', 'a new passphrase')
    await type('reset-confirm', 'a new passphrase')
    await submit('reset-form')
    expect(requests[0]?.body).not.toHaveProperty('rotateFeedTokens')
    expect(queryTestId('reset-feeds-rotated')).toBeNull()
  })
})
