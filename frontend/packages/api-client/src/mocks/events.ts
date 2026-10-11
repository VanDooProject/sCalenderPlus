import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import type { ErrorCode } from '../index'
import { mockProblem } from './auth'
import {
  mockCalendar,
  mockCalendars,
  mockEvent,
  mockOccurrences,
  mockResolveWallTime,
  mockSeriesEtag,
  type SeriesRecord,
} from './calendars'

type CalendarEvent = components['schemas']['EventResponse']
type EventTime = components['schemas']['EventTimeResponse']
type EventTimeRequest = components['schemas']['EventTimeRequest']
type Warning = components['schemas']['EventWarningResponse']

/**
 * Saving an event with this title simulates a concurrent change first (once per event): the
 * `PATCH` answers `412` and the event's location changes, so the conflict flow can be tested.
 */
export const mockConflictTitle = 'Edited elsewhere'

const rank = ['none', 'free_busy', 'read', 'contribute', 'edit', 'manage', 'owner']
const atLeast = (level: string, minimum: string) => rank.indexOf(level) >= rank.indexOf(minimum)

let counter = 1
const nextEtag = (id: string) => `"${id.slice(-4)}-${++counter}"`
const conflicted = new Set<string>()

function problem(code: ErrorCode, status: number, instance: string): Response {
  return new Response(JSON.stringify(mockProblem(code, status, instance)), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

function checkIfMatch(request: Request, etag: string, instance: string): Response | null {
  const ifMatch = request.headers.get('If-Match')
  if (!ifMatch) return problem('precondition_required', 428, instance)
  if (ifMatch !== '*' && ifMatch !== etag) return problem('precondition_failed', 412, instance)
  return null
}

/** A time value of a request as the api resolves it (DST gaps shift forward with a warning). */
function resolveTime(
  value: EventTimeRequest | null | undefined,
  zone: string,
  field: string,
  warnings: Warning[],
): EventTime {
  if (value?.date) return { date: value.date }
  const timeZone = value?.timeZone ?? zone
  const requested = (value?.dateTime ?? '').slice(0, 19)
  const resolved = mockResolveWallTime(requested, timeZone)
  if (resolved.shifted) {
    warnings.push({
      code: 'time_shifted_dst_gap',
      field,
      message: `${requested} does not exist in ${timeZone}; shifted to ${resolved.dateTime}.`,
      requested,
      resolved: resolved.dateTime,
      utc: resolved.utc,
    })
  }
  return { dateTime: resolved.dateTime, timeZone, utc: resolved.utc }
}

const json = (body: unknown, status = 200, etag?: string) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...(etag ? { ETag: etag } : {}) },
  })

/** Merge-patch semantics of the text members: empty string removes, null/absent keeps. */
const text = (value: string | null | undefined, current: string | null | undefined) =>
  value === '' ? null : (value ?? current ?? null)

function findSeries(id: string): SeriesRecord | undefined {
  return mockCalendars.series.find((s) => s.master.id === id)
}

/**
 * Stateful handlers for single events and series (create, read, merge-patch, delete, occurrence
 * changes, split) with ETags and `If-Match`, levels, frozen calendars and DST-gap warnings.
 */
export function eventHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  return [
    http.post('/api/v1/events', async ({ request }) => {
      const body = await request.json()
      const instance = '/api/v1/events'
      const calendar = mockCalendars.calendars.find((c) => c.id === body.calendarId)
      if (!calendar) return problem('not_found', 404, instance)
      if (!atLeast(calendar.myLevel, 'contribute')) {
        return problem('insufficient_permission', 403, instance)
      }
      if (calendar.frozen) return problem('calendar_frozen', 409, instance)
      const zone = body.start?.timeZone ?? calendar.defaultTimeZone ?? mockCalendar.defaultTimeZone
      const warnings: Warning[] = []
      const id = crypto.randomUUID()
      const event: CalendarEvent = {
        ...mockEvent,
        id,
        uid: body.uid ?? `${id}@scalenderplus`,
        calendarId: calendar.id,
        title: body.title ?? '',
        description: body.description || null,
        location: body.location || null,
        url: body.url ?? null,
        status: body.status ?? 'confirmed',
        transparency: body.transparency ?? 'opaque',
        color: body.color?.toLowerCase() ?? null,
        categories: body.categories ?? [],
        start: resolveTime(body.start, zone, 'start', warnings),
        end: resolveTime(body.end, zone, 'end', warnings),
        allDay: Boolean(body.start?.date),
        myLevel: 'manage',
        sequence: 0,
        etag: nextEtag(id),
      }
      mockCalendars.events.push(event)
      return json({ ...event, warnings: warnings.length ? warnings : undefined }, 201, event.etag!)
    }),
    http.get('/api/v1/events/{id}', ({ params }) => {
      const event = mockCalendars.events.find((e) => e.id === params.id)
      const series = findSeries(params.id)
      if (series) {
        const etag = mockSeriesEtag(series)
        return json({ ...series.master, etag }, 200, etag)
      }
      return event
        ? json(event, 200, event.etag ?? undefined)
        : problem('not_found', 404, `/api/v1/events/${params.id}`)
    }),
    http.patch('/api/v1/events/{id}', async ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}`
      const index = mockCalendars.events.findIndex((e) => e.id === params.id)
      const series = findSeries(params.id)
      const event = index >= 0 ? mockCalendars.events[index] : series?.master
      if (!event) return problem('not_found', 404, instance)
      if (!atLeast(event.myLevel, 'edit')) return problem('insufficient_permission', 403, instance)
      const etag = series ? mockSeriesEtag(series) : (event.etag ?? '')
      const patch = (await request.json()) as components['schemas']['UpdateEventRequest']
      if (patch.title === mockConflictTitle && !conflicted.has(event.id)) {
        // Someone else changed the event a moment ago.
        conflicted.add(event.id)
        const changed = { ...event, location: 'Pitch 3', etag: nextEtag(event.id) }
        if (series) {
          series.master = changed
          series.version = (series.version ?? 1) + 1
        } else mockCalendars.events[index] = changed
        return problem('precondition_failed', 412, instance)
      }
      const failed = checkIfMatch(request, etag, instance)
      if (failed) return failed
      const zone = event.start.timeZone ?? mockCalendar.defaultTimeZone
      const warnings: Warning[] = []
      const start = patch.start ? resolveTime(patch.start, zone, 'start', warnings) : event.start
      const end = patch.end
        ? resolveTime(patch.end, start.timeZone ?? zone, 'end', warnings)
        : event.end
      const updated: CalendarEvent = {
        ...event,
        title: patch.title ?? event.title,
        description: text(patch.description, event.description),
        location: text(patch.location, event.location),
        start,
        end,
        allDay: Boolean(start.date),
        sequence: (event.sequence ?? 0) + (patch.start || patch.end ? 1 : 0),
        etag: nextEtag(event.id),
      }
      if (series) {
        series.master = updated
        series.version = (series.version ?? 1) + 1
        const seriesEtag = mockSeriesEtag(series)
        return json({ ...updated, etag: seriesEtag }, 200, seriesEtag)
      }
      mockCalendars.events[index] = updated
      return json(
        { ...updated, warnings: warnings.length ? warnings : undefined },
        200,
        updated.etag!,
      )
    }),
    http.delete('/api/v1/events/{id}', ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}`
      const event = mockCalendars.events.find((e) => e.id === params.id)
      const series = findSeries(params.id)
      const target = event ?? series?.master
      if (!target) return problem('not_found', 404, instance)
      if (!atLeast(target.myLevel, 'edit')) return problem('insufficient_permission', 403, instance)
      const failed = checkIfMatch(
        request,
        series ? mockSeriesEtag(series) : (target.etag ?? ''),
        instance,
      )
      if (failed) return failed
      mockCalendars.events = mockCalendars.events.filter((e) => e.id !== params.id)
      mockCalendars.series = mockCalendars.series.filter((s) => s.master.id !== params.id)
      return new Response(null, { status: 204 })
    }),
    http.patch('/api/v1/events/{id}/occurrences/{recurrenceId}', async ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}/occurrences/${params.recurrenceId}`
      const series = findSeries(params.id)
      const occurrence = series
        ? mockOccurrences(series).find((o) => o.recurrenceId === params.recurrenceId)
        : undefined
      if (!series || !occurrence) return problem('not_found', 404, instance)
      if (!atLeast(series.master.myLevel, 'edit')) {
        return problem('insufficient_permission', 403, instance)
      }
      const failed = checkIfMatch(request, mockSeriesEtag(series), instance)
      if (failed) return failed
      const patch = (await request.json()) as components['schemas']['UpdateOccurrenceRequest']
      const zone = occurrence.start.timeZone ?? mockCalendar.defaultTimeZone
      const warnings: Warning[] = []
      const exception: Partial<CalendarEvent> = {
        ...series.exceptions?.[params.recurrenceId],
        ...(patch.title != null ? { title: patch.title } : {}),
        ...(patch.description != null ? { description: patch.description || null } : {}),
        ...(patch.location != null ? { location: patch.location || null } : {}),
        ...(patch.start ? { start: resolveTime(patch.start, zone, 'start', warnings) } : {}),
        ...(patch.end ? { end: resolveTime(patch.end, zone, 'end', warnings) } : {}),
      }
      series.exceptions = { ...series.exceptions, [params.recurrenceId]: exception }
      series.version = (series.version ?? 1) + 1
      const updated = mockOccurrences(series).find((o) => o.recurrenceId === params.recurrenceId)
      const etag = mockSeriesEtag(series)
      return json({ ...updated, warnings: warnings.length ? warnings : undefined, etag }, 200, etag)
    }),
    http.delete('/api/v1/events/{id}/occurrences/{recurrenceId}', ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}/occurrences/${params.recurrenceId}`
      const series = findSeries(params.id)
      if (!series || !mockOccurrences(series).some((o) => o.recurrenceId === params.recurrenceId)) {
        return problem('not_found', 404, instance)
      }
      const failed = checkIfMatch(request, mockSeriesEtag(series), instance)
      if (failed) return failed
      series.cancelled = [...series.cancelled, params.recurrenceId]
      series.version = (series.version ?? 1) + 1
      return new Response(null, { status: 204, headers: { ETag: mockSeriesEtag(series) } })
    }),
    http.post('/api/v1/events/{id}/split', async ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}/split`
      const series = findSeries(params.id)
      const body = await request.json()
      if (!series) return problem('not_found', 404, instance)
      const failed = checkIfMatch(request, mockSeriesEtag(series), instance)
      if (failed) return failed
      // Every occurrence of the rule by index (cancelled ones included), as COUNT counts them.
      const all = mockOccurrences({ ...series, cancelled: [], exceptions: {} })
      const at = all.findIndex((o) => o.recurrenceId === body.recurrenceId)
      if (at < 0) return problem('not_found', 404, instance)
      if (at === 0) return problem('bad_request', 400, instance)
      const occurrence = all[at]!
      const zone = occurrence.start.timeZone ?? mockCalendar.defaultTimeZone
      const warnings: Warning[] = []
      const id = crypto.randomUUID()
      const master: CalendarEvent = {
        ...series.master,
        id,
        uid: `${id}@scalenderplus`,
        relatedTo: series.master.uid,
        title: body.title ?? series.master.title,
        description: text(body.description, series.master.description),
        location: text(body.location, series.master.location),
        start: body.start ? resolveTime(body.start, zone, 'start', warnings) : occurrence.start,
        end: body.end ? resolveTime(body.end, zone, 'end', warnings) : occurrence.end,
        recurrence: { rrule: `FREQ=WEEKLY;COUNT=${series.count - at}` },
      }
      mockCalendars.series.push({
        master,
        everyDays: series.everyDays,
        count: series.count - at,
        cancelled: [],
        version: 1,
      })
      series.count = at
      series.master = { ...series.master, recurrence: { rrule: `FREQ=WEEKLY;COUNT=${at}` } }
      series.version = (series.version ?? 1) + 1
      const created = findSeries(id)!
      return json({ ...master, etag: mockSeriesEtag(created) }, 201, mockSeriesEtag(created))
    }),
  ]
}
