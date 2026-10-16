import { Temporal } from 'temporal-polyfill'
import type { components, EventResponse } from '@scalenderplus/api-client'

type CreateEventRequest = components['schemas']['CreateEventRequest']
type UpdateEventRequest = components['schemas']['UpdateEventRequest']
type UpdateOccurrenceRequest = components['schemas']['UpdateOccurrenceRequest']
type EventTimeRequest = components['schemas']['EventTimeRequest']

/**
 * Values of the event dialog. Timed events are wall times in one IANA zone (api.md: one zone per
 * event); all-day events are dates with an **inclusive** last day here (the api's end is exclusive).
 */
export interface EventFormValues {
  title: string
  /** What busy-only viewers see instead of "Busy" (privacy-levels.md §5); empty: none. */
  coverTitle: string
  calendarId: string
  allDay: boolean
  startDate: string
  startTime: string
  endDate: string
  endTime: string
  timeZone: string
  location: string
  description: string
}

const plusDays = (date: string, days: number) =>
  Temporal.PlainDate.from(date).add({ days }).toString()

/** `HH:MM` of a wall-clock date-time (`YYYY-MM-DDTHH:MM[:SS]`). */
const timeOf = (dateTime: string) => dateTime.slice(11, 16)

/** Values of a new event from a selected range (FullCalendar's `startStr`/`endStr`). */
export function newEventValues(input: {
  calendarId: string
  timeZone: string
  start: string
  end?: string
  allDay: boolean
}): EventFormValues {
  const base = {
    title: '',
    coverTitle: '',
    calendarId: input.calendarId,
    timeZone: input.timeZone,
  }
  const startDate = input.start.slice(0, 10)
  if (input.allDay) {
    // A selection's end is exclusive; the form shows the last day.
    const end = input.end ? plusDays(input.end.slice(0, 10), -1) : startDate
    return {
      ...base,
      allDay: true,
      startDate,
      startTime: '09:00',
      endDate: end < startDate ? startDate : end,
      endTime: '10:00',
      location: '',
      description: '',
    }
  }
  const start = Temporal.PlainDateTime.from(input.start.slice(0, 16))
  const end = input.end
    ? Temporal.PlainDateTime.from(input.end.slice(0, 16))
    : start.add({ hours: 1 })
  return {
    ...base,
    allDay: false,
    startDate,
    startTime: timeOf(start.toString()),
    endDate: end.toPlainDate().toString(),
    endTime: timeOf(end.toString()),
    location: '',
    description: '',
  }
}

/** Values of an existing event (or occurrence) in its own zone. */
export function eventValues(event: EventResponse, fallbackZone: string): EventFormValues {
  const base = {
    title: event.title ?? '',
    coverTitle: event.coverTitle ?? '',
    calendarId: event.calendarId,
    location: event.location ?? '',
    description: event.description ?? '',
  }
  if (event.allDay) {
    const startDate = event.start.date ?? ''
    const endDate = event.end.date ? plusDays(event.end.date, -1) : startDate
    return {
      ...base,
      allDay: true,
      startDate,
      startTime: '09:00',
      endDate: endDate < startDate ? startDate : endDate,
      endTime: '10:00',
      timeZone: event.start.timeZone ?? fallbackZone,
    }
  }
  const start = event.start.dateTime ?? ''
  const end = event.end.dateTime ?? ''
  return {
    ...base,
    allDay: false,
    startDate: start.slice(0, 10),
    startTime: timeOf(start),
    endDate: end.slice(0, 10),
    endTime: timeOf(end),
    timeZone: event.start.timeZone ?? fallbackZone,
  }
}

/** The api times of the form: dates with an exclusive end, or wall times in the chosen zone. */
export function eventTimes(v: EventFormValues): { start: EventTimeRequest; end: EventTimeRequest } {
  if (v.allDay) return { start: { date: v.startDate }, end: { date: plusDays(v.endDate, 1) } }
  return {
    start: { dateTime: `${v.startDate}T${v.startTime}:00`, timeZone: v.timeZone },
    end: { dateTime: `${v.endDate}T${v.endTime}:00`, timeZone: v.timeZone },
  }
}

/** Whether the end is not after the start (all-day: the last day before the first). */
export function endBeforeStart(v: EventFormValues): boolean {
  if (v.allDay) return v.endDate < v.startDate
  return `${v.endDate}T${v.endTime}` <= `${v.startDate}T${v.startTime}`
}

/** `POST /events` body of the form (empty texts left out). */
export function toCreateRequest(v: EventFormValues): CreateEventRequest {
  return {
    calendarId: v.calendarId,
    title: v.title.trim(),
    ...(v.coverTitle.trim() ? { coverTitle: v.coverTitle.trim() } : {}),
    ...eventTimes(v),
    ...(v.location.trim() ? { location: v.location.trim() } : {}),
    ...(v.description.trim() ? { description: v.description.trim() } : {}),
  }
}

const timeFields = ['allDay', 'startDate', 'startTime', 'endDate', 'endTime', 'timeZone'] as const

/**
 * JSON Merge Patch of the changed members (api.md: absent members stay; an empty `description` or
 * `location` removes it, a cleared cover title is sent as `null`). Times are sent as a pair whenever one of them changed (switching between
 * timed and all-day needs both).
 */
export function toMergePatch(initial: EventFormValues, v: EventFormValues): UpdateEventRequest {
  const patch: UpdateEventRequest = {}
  if (v.title.trim() !== initial.title.trim()) patch.title = v.title.trim()
  if (v.coverTitle.trim() !== initial.coverTitle.trim()) {
    patch.coverTitle = v.coverTitle.trim() || null
  }
  if (v.location.trim() !== initial.location.trim()) patch.location = v.location.trim()
  if (v.description.trim() !== initial.description.trim()) patch.description = v.description.trim()
  const timesChanged = v.allDay
    ? (['allDay', 'startDate', 'endDate'] as const).some((k) => v[k] !== initial[k])
    : timeFields.some((k) => v[k] !== initial[k])
  if (timesChanged) Object.assign(patch, eventTimes(v))
  return patch
}

/** The patch of one occurrence (the members `PATCH …/occurrences/{rid}` takes). */
export function toOccurrencePatch(
  initial: EventFormValues,
  v: EventFormValues,
): UpdateOccurrenceRequest {
  const { title, coverTitle, location, description, start, end } = toMergePatch(initial, v)
  return Object.fromEntries(
    Object.entries({ title, coverTitle, location, description, start, end }).filter(
      ([, x]) => x !== undefined,
    ),
  )
}

/**
 * Whether "this and following" can be offered: an occurrence of the rule (not an extra `rdate`, not
 * shifted by a DST gap — the api answers `400` for those, review 2026-10 F3).
 */
export function isRuleOccurrence(event: EventResponse): boolean {
  if (!event.recurrenceId || !event.recurrence?.rrule) return false
  const zone = event.start.timeZone
  if (event.allDay || !zone) {
    return !(event.recurrence.rdates ?? []).includes(event.recurrenceId)
  }
  let nominal: Temporal.PlainDateTime
  try {
    nominal = Temporal.Instant.from(event.recurrenceId).toZonedDateTimeISO(zone).toPlainDateTime()
  } catch {
    return false
  }
  const wall = nominal.toString({ smallestUnit: 'second' })
  if ((event.recurrence.rdates ?? []).some((d) => d.slice(0, 19) === wall)) return false
  // An unmodified occurrence whose start differs from its recurrence id was moved by a DST gap.
  if (!event.modified && event.start.utc) {
    return Temporal.Instant.compare(event.start.utc, event.recurrenceId) === 0
  }
  return true
}

/**
 * New times of an event moved or resized in FullCalendar: its wall times there (`startStr`/`endStr`
 * in the view's zone) as wall times in the event's own zone; all-day events as dates.
 */
export function droppedTimes(input: {
  start: string
  end: string
  allDay: boolean
  viewZone: string
  eventZone: string
}): { start: EventTimeRequest; end: EventTimeRequest } {
  if (input.allDay) {
    return { start: { date: input.start.slice(0, 10) }, end: { date: input.end.slice(0, 10) } }
  }
  const convert = (value: string) =>
    Temporal.PlainDateTime.from(value.slice(0, 19))
      .toZonedDateTime(input.viewZone)
      .withTimeZone(input.eventZone)
      .toPlainDateTime()
      .toString({ smallestUnit: 'second' })
  return {
    start: { dateTime: convert(input.start), timeZone: input.eventZone },
    end: { dateTime: convert(input.end), timeZone: input.eventZone },
  }
}

/**
 * "All events" of a series edited from one occurrence: the series' first occurrence moves by the
 * same amount as this occurrence did, with the new duration.
 */
export function shiftSeriesTimes(
  master: EventResponse,
  before: EventFormValues,
  after: EventFormValues,
): { start: EventTimeRequest; end: EventTimeRequest } {
  const times = eventTimes(after)
  if (after.allDay !== before.allDay || after.allDay !== master.allDay) {
    // Switching kinds: the first occurrence's date with the new times.
    const first = (master.allDay ? master.start.date : master.start.dateTime?.slice(0, 10)) ?? ''
    const shifted = eventTimes({
      ...after,
      startDate: first,
      endDate: Temporal.PlainDate.from(first)
        .add(Temporal.PlainDate.from(after.startDate).until(after.endDate))
        .toString(),
    })
    return shifted
  }
  if (after.allDay) {
    const delta = Temporal.PlainDate.from(before.startDate).until(after.startDate)
    const length = Temporal.PlainDate.from(times.start.date!).until(times.end.date!)
    const start = Temporal.PlainDate.from(master.start.date ?? after.startDate).add(delta)
    return { start: { date: start.toString() }, end: { date: start.add(length).toString() } }
  }
  const wall = (date: string, time: string) => Temporal.PlainDateTime.from(`${date}T${time}`)
  const delta = wall(before.startDate, before.startTime).until(
    wall(after.startDate, after.startTime),
  )
  const length = wall(after.startDate, after.startTime).until(wall(after.endDate, after.endTime))
  const start = Temporal.PlainDateTime.from(master.start.dateTime ?? times.start.dateTime!).add(
    delta,
  )
  const second = { smallestUnit: 'second' } as const
  return {
    start: { dateTime: start.toString(second), timeZone: after.timeZone },
    end: { dateTime: start.add(length).toString(second), timeZone: after.timeZone },
  }
}
