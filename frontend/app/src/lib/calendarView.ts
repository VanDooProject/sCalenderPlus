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

/** Relative luminance of a `#rrggbb` color (WCAG 2). */
function luminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const [r, g, b] = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * (r ?? 0) + 0.7152 * (g ?? 0) + 0.0722 * (b ?? 0)
}

/** White or near-black text, whichever contrasts more with `background` (`#rrggbb`). */
export function contrastText(background: string): string {
  if (!/^#[0-9a-f]{6}$/i.test(background)) return '#ffffff'
  const l = luminance(background)
  const dark = luminance('#111827')
  return 1.05 / (l + 0.05) >= (l + 0.05) / (dark + 0.05) ? '#ffffff' : '#111827'
}

export interface MappedEvent {
  id: string
  title: string
  start: string
  end: string
  allDay: boolean
  color: string
  contrastColor: string
  className: string
  interactive: boolean
  /** Drag and resize (FullCalendar per event): only events the user may change. */
  editable: boolean
  extendedProps: { source: EventResponse; busy: boolean; editable: boolean }
}

/**
 * An api event as a FullCalendar event, or null when there is nothing to draw: a busy item without
 * times (a "free" exception of the busy projection) and transparent busy items are free time.
 * Busy items never carry the real title (the projection has none): their title is the event's
 * cover title when set (an occurrence's `""`: none), else empty and the UI renders "Busy".
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
  const color = event.sharedWithMe ? sharedColor : colorOf(event.calendarId)
  return {
    id: event.occurrenceId ?? event.id,
    title: busy ? event.coverTitle || '' : (event.title ?? ''),
    start,
    end,
    allDay: event.allDay,
    color,
    contrastColor: contrastText(color),
    className: busy ? 'scal-busy' : editable ? 'scal-editable' : 'scal-readonly',
    interactive: !busy,
    editable,
    extendedProps: { source: event, busy, editable },
  }
}
