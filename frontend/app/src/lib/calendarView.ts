import { Temporal } from 'temporal-polyfill'
import type { EventResponse } from '@scalenderplus/api-client'
import { sharedColor } from './calendarColors'

/** The views of the calendar page (`?view=`). */
export const calendarViews = ['month', 'week', 'day', 'agenda'] as const
export type CalendarViewName = (typeof calendarViews)[number]

/** FullCalendar view per page view; the agenda lists a month. */
export const fullCalendarViews: Record<CalendarViewName, string> = {
  month: 'dayGridMonth',
  week: 'timeGridWeek',
  day: 'timeGridDay',
  agenda: 'listMonth',
}

export function parseView(value: unknown): CalendarViewName | null {
  return typeof value === 'string' && (calendarViews as readonly string[]).includes(value)
    ? (value as CalendarViewName)
    : null
}

/** `?date=YYYY-MM-DD`, or null when missing or invalid. */
export function parseDate(value: unknown): Temporal.PlainDate | null {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
  try {
    return Temporal.PlainDate.from(value, { overflow: 'reject' })
  } catch {
    return null
  }
}

/** Today in a time zone (falls back to UTC for an unknown zone). */
export function today(timeZone: string): Temporal.PlainDate {
  try {
    return Temporal.Now.plainDateISO(timeZone)
  } catch {
    return Temporal.Now.plainDateISO('UTC')
  }
}

const weekdayIndex: Record<string, number> = {
  sunday: 0,
  monday: 1,
  tuesday: 2,
  wednesday: 3,
  thursday: 4,
  friday: 5,
  saturday: 6,
}

/** FullCalendar's `firstDay` (0 = Sunday) for the profile's `weekStart` (default Monday). */
export function firstDayOf(weekStart: string | undefined): number {
  return weekdayIndex[weekStart ?? ''] ?? 1
}

/** The first day of the week containing `date`. */
export function startOfWeek(date: Temporal.PlainDate, firstDay: number): Temporal.PlainDate {
  // Temporal: dayOfWeek 1 = Monday … 7 = Sunday; FullCalendar: 0 = Sunday.
  const back = (date.dayOfWeek % 7) - firstDay
  return date.subtract({ days: (back + 7) % 7 })
}

/** The dates a view shows: `[start, end)` as plain dates (month: six full weeks, like FullCalendar). */
export function visibleDates(
  view: CalendarViewName,
  date: Temporal.PlainDate,
  firstDay: number,
): { start: Temporal.PlainDate; end: Temporal.PlainDate } {
  switch (view) {
    case 'day':
      return { start: date, end: date.add({ days: 1 }) }
    case 'week': {
      const start = startOfWeek(date, firstDay)
      return { start, end: start.add({ days: 7 }) }
    }
    case 'month': {
      const start = startOfWeek(date.with({ day: 1 }), firstDay)
      return { start, end: start.add({ days: 42 }) }
    }
    case 'agenda': {
      const start = date.with({ day: 1 })
      return { start, end: start.add({ months: 1 }) }
    }
  }
}

/** The event window of a view in the user's zone, as the instants of `GET /events?from&to`. */
export function visibleRange(
  view: CalendarViewName,
  date: Temporal.PlainDate,
  firstDay: number,
  timeZone: string,
): { from: string; to: string } {
  const { start, end } = visibleDates(view, date, firstDay)
  const instant = (d: Temporal.PlainDate) =>
    d.toZonedDateTime({ timeZone }).toInstant().toString({ smallestUnit: 'second' })
  return { from: instant(start), to: instant(end) }
}

/** The date one step before or after (`direction` −1/1) in a view. */
export function shiftDate(
  view: CalendarViewName,
  date: Temporal.PlainDate,
  direction: 1 | -1,
): Temporal.PlainDate {
  switch (view) {
    case 'day':
      return date.add({ days: direction })
    case 'week':
      return date.add({ weeks: direction })
    default:
      return date.add({ months: direction })
  }
}

export interface EventsWindow {
  from: string
  to: string
  timeZone: string
  /** Shown calendars, sorted (part of the cache key). */
  calendarIds: string[]
  /** Include "Shared with me" events (calendars the user cannot see). */
  sharedWithMe: boolean
}

/** Cache key of a window: below `['events']`, so any event change can invalidate all windows. */
export function eventsQueryKey(window: EventsWindow) {
  return [
    'events',
    {
      from: window.from,
      to: window.to,
      timeZone: window.timeZone,
      calendarIds: [...window.calendarIds].sort(),
      sharedWithMe: window.sharedWithMe,
    },
  ] as const
}

/**
 * The query of a window. `calendarIds` narrows the api to those calendars, which also drops
 * "Shared with me" events (they belong to calendars the user cannot see); with "Shared with me" on,
 * the whole window is fetched and the hidden calendars are filtered out on the client.
 */
export function eventsQueryParams(window: EventsWindow) {
  return {
    from: window.from,
    to: window.to,
    timeZone: window.timeZone,
    expand: 'occurrences',
    ...(window.sharedWithMe ? {} : { calendarIds: [...window.calendarIds].sort() }),
  }
}

/** Keeps the events of shown calendars and, when enabled, those shared with the user. */
export function visibleEvents(items: EventResponse[], window: EventsWindow): EventResponse[] {
  const shown = new Set(window.calendarIds)
  return items.filter((e) => (e.sharedWithMe ? window.sharedWithMe : shown.has(e.calendarId)))
}

/** Event levels that may change an event (permissions.md: `edit`, `manage`). */
const editableLevels = new Set(['edit', 'manage', 'owner'])

export const isBusyOnly = (event: EventResponse) =>
  event.myLevel === 'free_busy' || event.title === null

export const isEditable = (event: EventResponse) => editableLevels.has(event.myLevel)

export interface MappedEvent {
  id: string
  title: string
  start: string
  end: string
  allDay: boolean
  color: string
  className: string
  interactive: boolean
  extendedProps: { source: EventResponse; busy: boolean; editable: boolean }
}

/**
 * An api event as a FullCalendar event, or null when there is nothing to draw: a busy item without
 * times (a "free" exception of the busy projection) and transparent busy items are free time.
 * Busy items never carry a title (the projection has none; the UI renders the localized "Busy").
 */
export function toCalendarEvent(
  event: EventResponse,
  colorOf: (calendarId: string) => string,
): MappedEvent | null {
  const busy = isBusyOnly(event)
  if (busy && event.transparency === 'transparent') return null
  const start = event.allDay ? event.start?.date : event.start?.utc
  const end = event.allDay ? event.end?.date : event.end?.utc
  if (!start || !end) return null
  const editable = !busy && isEditable(event)
  return {
    id: event.occurrenceId ?? event.id,
    title: busy ? '' : (event.title ?? ''),
    start,
    end,
    allDay: event.allDay,
    color: event.sharedWithMe ? sharedColor : colorOf(event.calendarId),
    className: busy ? 'scal-busy' : editable ? 'scal-editable' : 'scal-readonly',
    interactive: !busy,
    extendedProps: { source: event, busy, editable },
  }
}
