import { afterEach, describe, expect, it } from 'vitest'
import { Temporal } from 'temporal-polyfill'
import type { EventResponse } from '@scalenderplus/api-client'
import { mockCalendar, mockCalendars, mockPersonalCalendar } from '@scalenderplus/api-client/mocks'
import {
  contrastText,
  eventsQueryKey,
  eventsQueryParams,
  firstDayOf,
  parseDate,
  parseView,
  shiftDate,
  toCalendarEvent,
  visibleEvents,
  visibleRange,
  type EventsWindow,
} from '@/lib/calendarView'
import { byTestId, mountApp, queryTestId, resetDom, until } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(resetDom)
const date = (value: string) => Temporal.PlainDate.from(value)

describe('calendar range', () => {
  it('reads view and date from the URL', () => {
    expect(parseView('month')).toBe('month')
    expect(parseView('year')).toBeNull()
    expect(parseDate('2026-11-04')?.toString()).toBe('2026-11-04')
    expect(parseDate('2026-02-30')).toBeNull()
    expect(parseDate('tomorrow')).toBeNull()
    expect(firstDayOf('sunday')).toBe(0)
    expect(firstDayOf('monday')).toBe(1)
    expect(firstDayOf(undefined)).toBe(1)
  })

  it('computes the window of each view in the user zone', () => {
    const monday = firstDayOf('monday')
    expect(visibleRange('week', date('2026-11-04'), monday, 'Europe/Berlin')).toEqual({
      from: '2026-11-01T23:00:00Z',
      to: '2026-11-08T23:00:00Z',
    })
    expect(visibleRange('day', date('2026-11-04'), monday, 'America/New_York')).toEqual({
      from: '2026-11-04T05:00:00Z',
      to: '2026-11-05T05:00:00Z',
    })
    // Six weeks from the week of the 1st (Sunday start).
    expect(visibleRange('month', date('2026-11-18'), firstDayOf('sunday'), 'UTC')).toEqual({
      from: '2026-11-01T00:00:00Z',
      to: '2026-12-13T00:00:00Z',
    })
    expect(visibleRange('agenda', date('2026-11-18'), monday, 'UTC')).toEqual({
      from: '2026-11-01T00:00:00Z',
      to: '2026-12-01T00:00:00Z',
    })
    // The week of the switch to winter time is 169 hours long.
    expect(visibleRange('week', date('2026-10-21'), monday, 'Europe/Berlin')).toEqual({
      from: '2026-10-18T22:00:00Z',
      to: '2026-10-25T23:00:00Z',
    })
  })

  it('steps by the view', () => {
    expect(shiftDate('day', date('2026-11-30'), 1).toString()).toBe('2026-12-01')
    expect(shiftDate('week', date('2026-11-30'), -1).toString()).toBe('2026-11-23')
    expect(shiftDate('month', date('2026-01-31'), 1).toString()).toBe('2026-02-28')
  })

  it('keys the query by window and shown calendars', () => {
    const window: EventsWindow = {
      from: '2026-11-01T23:00:00Z',
      to: '2026-11-08T23:00:00Z',
      timeZone: 'Europe/Berlin',
      calendarIds: ['b', 'a'],
      sharedWithMe: false,
    }
    const key = eventsQueryKey(window)
    expect(key[0]).toBe('events')
    expect(key).toEqual(eventsQueryKey({ ...window, calendarIds: ['a', 'b'] }))
    expect(key).not.toEqual(eventsQueryKey({ ...window, calendarIds: ['a'] }))
    expect(key).not.toEqual(eventsQueryKey({ ...window, to: '2026-11-09T23:00:00Z' }))
    expect(key).not.toEqual(eventsQueryKey({ ...window, sharedWithMe: true }))

    expect(eventsQueryParams(window)).toEqual({
      from: window.from,
      to: window.to,
      timeZone: 'Europe/Berlin',
      expand: 'occurrences',
      calendarIds: ['a', 'b'],
    })
    // "Shared with me" events belong to other calendars: the window is fetched unnarrowed.
    expect(eventsQueryParams({ ...window, sharedWithMe: true })).not.toHaveProperty('calendarIds')
  })
})

describe('event mapping', () => {
  const colorOf = (id: string) => (id === 'cal-1' ? '#123456' : '#654321')
  const timed: EventResponse = {
    id: 'e1',
    calendarId: 'cal-1',
    title: 'Board meeting',
    start: {
      dateTime: '2026-11-02T18:00:00',
      timeZone: 'Europe/Berlin',
      utc: '2026-11-02T17:00:00Z',
    },
    end: {
      dateTime: '2026-11-02T20:00:00',
      timeZone: 'Europe/Berlin',
      utc: '2026-11-02T19:00:00Z',
    },
    allDay: false,
    transparency: 'opaque',
    myLevel: 'manage',
  }

  it('maps timed events by their instants and all-day events by their dates', () => {
    expect(toCalendarEvent(timed, colorOf)).toMatchObject({
      id: 'e1',
      title: 'Board meeting',
      start: '2026-11-02T17:00:00Z',
      end: '2026-11-02T19:00:00Z',
      allDay: false,
      color: '#123456',
      className: 'scal-editable',
      interactive: true,
    })
    const allDay = toCalendarEvent(
      { ...timed, allDay: true, start: { date: '2026-11-04' }, end: { date: '2026-11-06' } },
      colorOf,
    )
    expect(allDay).toMatchObject({ start: '2026-11-04', end: '2026-11-06', allDay: true })
  })

  it('marks read-only events and colors shared ones neutrally', () => {
    expect(toCalendarEvent({ ...timed, myLevel: 'read' }, colorOf)?.className).toBe('scal-readonly')
    expect(toCalendarEvent({ ...timed, sharedWithMe: true }, colorOf)?.color).toBe('#64748b')
    expect(
      toCalendarEvent({ ...timed, occurrenceId: 'e1:2026-11-02T17:00:00Z' }, colorOf)?.id,
    ).toBe('e1:2026-11-02T17:00:00Z')
  })

  it('renders busy items as untitled, non-interactive blocks', () => {
    const busy: EventResponse = { ...timed, title: null, myLevel: 'free_busy' }
    const mapped = toCalendarEvent(busy, colorOf)
    expect(mapped).toMatchObject({ title: '', className: 'scal-busy', interactive: false })
    expect(mapped?.extendedProps.busy).toBe(true)
    expect(mapped?.extendedProps.editable).toBe(false)
  })

  it('draws nothing for free time of the busy projection', () => {
    const free: EventResponse = {
      ...timed,
      title: null,
      myLevel: 'free_busy',
      transparency: 'transparent',
    }
    expect(toCalendarEvent(free, colorOf)).toBeNull()
    // A "free" exception comes without its times.
    const noTimes = { ...timed, title: null, myLevel: 'free_busy', start: {}, end: {} }
    expect(toCalendarEvent(noTimes, colorOf)).toBeNull()
  })

  it('picks readable text for the calendar color', () => {
    expect(contrastText('#4f46e5')).toBe('#ffffff')
    expect(contrastText('#16a34a')).toBe('#111827')
    expect(contrastText('#d97706')).toBe('#111827')
    expect(toCalendarEvent(timed, () => '#16a34a')?.contrastColor).toBe('#111827')
  })

  it('keeps shown calendars and, when enabled, events shared with me', () => {
    const shared = { ...timed, id: 'e2', calendarId: 'other', sharedWithMe: true }
    const hidden = { ...timed, id: 'e3', calendarId: 'cal-2' }
    const window: EventsWindow = {
      from: '',
      to: '',
      timeZone: 'UTC',
      calendarIds: ['cal-1'],
      sharedWithMe: true,
    }
    expect(visibleEvents([timed, shared, hidden], window).map((e) => e.id)).toEqual(['e1', 'e2'])
    expect(
      visibleEvents([timed, shared, hidden], { ...window, sharedWithMe: false }).map((e) => e.id),
    ).toEqual(['e1'])
  })
})

describe('calendar page', () => {
  it('hides a calendar through the prefs endpoint with If-Match', async () => {
    const ifMatch: (string | null)[] = []
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'PUT' && request.url.endsWith('/prefs')) {
        ifMatch.push(request.headers.get('If-Match'))
      }
    })
    await mountApp('/calendar?view=week')
    const toggle = () => byTestId(`calendar-toggle-${mockCalendar.id}`) as HTMLInputElement
    expect(toggle().checked).toBe(true)

    toggle().click()
    await until(() => mockCalendars.prefs[mockCalendar.id]?.hidden === true)
    expect(toggle().checked).toBe(false)

    toggle().click()
    await until(() => mockCalendars.prefs[mockCalendar.id]?.hidden === false)
    expect(ifMatch).toEqual(['*', `"prefs-${mockCalendar.id.slice(-4)}-1"`])
    expect(mockCalendars.prefs[mockPersonalCalendar.id]).toBeUndefined()
  })

  it('tells the user when the window was truncated', async () => {
    mockCalendars.truncateAt = 1
    await mountApp('/calendar?view=month')
    await until(() => queryTestId('calendar-truncated') !== null)
    expect(byTestId('calendar-truncated').textContent).toContain('too many events')
  })
})
