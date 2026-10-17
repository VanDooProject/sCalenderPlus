import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import type { ErrorCode } from '../index'
import { mockProblem } from './auth'
import { mockCalendars } from './calendars'

type IcsUploadItem = components['schemas']['IcsUploadItem']
type IcsUploadResponse = components['schemas']['IcsUploadResponse']
type EventTime = components['schemas']['EventTimeResponse']

/** UIDs imported per calendar by the mock's commits (a re-upload reports them as duplicates). */
export const mockImports: { uids: Record<string, string[]> } = { uids: {} }

export function resetMockImports(): void {
  mockImports.uids = {}
}

const maxBytes = 5 * 1024 * 1024
const maxEvents = 5000
const contributors = ['contribute', 'edit', 'manage', 'owner']

/** Unfolded content lines of the components named `name` (a light reader: enough for the mock's report). */
function components(text: string, name: string): string[][] {
  const lines = text
    .replace(/\r\n/g, '\n')
    .replace(/\n[ \t]/g, '')
    .split('\n')
  const result: string[][] = []
  let current: string[] | null = null
  for (const line of lines) {
    if (line === `BEGIN:${name}`) current = []
    else if (line === `END:${name}`) {
      if (current) result.push(current)
      current = null
    } else current?.push(line)
  }
  return result
}

function property(lines: string[], name: string): { params: string; value: string } | undefined {
  const line = lines.find((l) => l.startsWith(`${name}:`) || l.startsWith(`${name};`))
  if (!line) return undefined
  const colon = line.indexOf(':')
  return { params: line.slice(name.length, colon), value: line.slice(colon + 1) }
}

function time(lines: string[], name: string): EventTime | null {
  const p = property(lines, name)
  if (!p) return null
  const v = p.value
  if (/^\d{8}$/.test(v)) return { date: `${v.slice(0, 4)}-${v.slice(4, 6)}-${v.slice(6, 8)}` }
  const m = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})?(Z?)$/.exec(v)
  if (!m) return null
  const zone = /TZID=("?)([^;:"]+)\1/.exec(p.params)?.[2] ?? 'UTC'
  return { dateTime: `${m[1]}-${m[2]}-${m[3]}T${m[4]}:${m[5]}:${m[6] ?? '00'}`, timeZone: zone }
}

/** The report of the api for a file, from a light reading (no recurrence expansion, no zone mapping). */
export function mockIcsReport(
  calendarId: string,
  text: string,
  dryRun: boolean,
): IcsUploadResponse {
  const vevents = components(text, 'VEVENT')
  const existing = new Set(mockImports.uids[calendarId] ?? [])
  const seen = new Set<string>()
  const items: IcsUploadItem[] = []
  for (const lines of vevents.filter((l) => !property(l, 'RECURRENCE-ID'))) {
    const uid = property(lines, 'UID')?.value ?? `ics-mock-${items.length}@import.scalenderplus`
    const summary = property(lines, 'SUMMARY')?.value.replace(/\\([,;\\])/g, '$1')
    const start = time(lines, 'DTSTART')
    const overrides = vevents.filter(
      (l) => property(l, 'RECURRENCE-ID') && property(l, 'UID')?.value === uid,
    )
    const cancelledOverrides = overrides.filter(
      (l) => property(l, 'STATUS')?.value === 'CANCELLED',
    ).length
    const exdates = lines
      .filter((l) => l.startsWith('EXDATE'))
      .reduce((n, l) => n + l.slice(l.indexOf(':') + 1).split(',').length, 0)
    const rrule = property(lines, 'RRULE')?.value ?? null
    const warnings: string[] = []
    if (!summary) warnings.push('title_missing')
    if (['PRIVATE', 'CONFIDENTIAL'].includes(property(lines, 'CLASS')?.value ?? ''))
      warnings.push('private_event')
    let action = 'create'
    let reason: string | null = null
    if (property(lines, 'STATUS')?.value === 'CANCELLED') [action, reason] = ['skip', 'cancelled']
    else if (!start) [action, reason] = ['skip', 'start_missing']
    else if (rrule && /FREQ=(HOURLY|MINUTELY|SECONDLY)|BYHOUR|BYMINUTE/.test(rrule))
      [action, reason] = ['skip', 'recurrence_not_supported']
    else if (seen.has(uid)) [action, reason] = ['duplicate', 'duplicate_in_file']
    else if (existing.has(uid)) [action, reason] = ['duplicate', 'exists']
    seen.add(uid)
    items.push({
      uid,
      title: summary ?? '(No title)',
      start,
      end: time(lines, 'DTEND') ?? start,
      allDay: !!start?.date,
      rrule: action === 'skip' ? null : rrule,
      modifiedOccurrences: rrule ? overrides.length - cancelledOverrides : 0,
      cancelledOccurrences: rrule ? exdates + cancelledOverrides : 0,
      action,
      reason,
      warnings: action === 'create' ? warnings : [],
    })
  }
  const created = items.filter((i) => i.action === 'create')
  if (!dryRun) mockImports.uids[calendarId] = [...existing, ...created.map((i) => i.uid)]
  const counted = new Map<string, number>()
  for (const code of created.flatMap((i) => i.warnings))
    counted.set(code, (counted.get(code) ?? 0) + 1)
  return {
    calendarId,
    dryRun,
    counts: {
      total: items.length,
      created: created.length,
      duplicates: items.filter((i) => i.action === 'duplicate').length,
      skipped: items.filter((i) => i.action === 'skip').length,
      series: created.filter((i) => i.rrule).length,
      modifiedOccurrences: created.reduce((n, i) => n + i.modifiedOccurrences, 0),
      cancelledOccurrences: created.reduce((n, i) => n + i.cancelledOccurrences, 0),
    },
    warnings: [...counted]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([code, count]) => ({ code, count })),
    items,
  }
}

/**
 * `POST /api/v1/calendars/{id}/ics-upload` like the api: contribute or more on a visible, unfrozen calendar,
 * `text/calendar` up to 5 MB and 5,000 VEVENTs; a commit remembers the UIDs so a re-upload is all duplicates.
 */
export function importHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  const problem = (code: ErrorCode, status: number, instance: string, extras = {}) =>
    new Response(JSON.stringify(mockProblem(code, status, instance, extras)), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    })

  return [
    http.post('/api/v1/calendars/{id}/ics-upload', async ({ params, query, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}/ics-upload`
      const calendar = mockCalendars.calendars.find((c) => c.id === params.id)
      if (!calendar) return problem('not_found', 404, instance)
      if (!contributors.includes(calendar.myLevel))
        return problem('insufficient_permission', 403, instance)
      if (calendar.frozen) return problem('calendar_frozen', 409, instance)
      if (!(request.headers.get('Content-Type') ?? '').startsWith('text/calendar'))
        return problem('unsupported_media_type', 415, instance)
      const text = await request.text()
      if (new TextEncoder().encode(text).length > maxBytes)
        return problem('payload_too_large', 413, instance)
      if (!/^\s*BEGIN:VCALENDAR/.test(text) || !/END:VCALENDAR\s*$/.test(text))
        return problem('ics_invalid', 422, instance)
      if ((text.match(/^BEGIN:VEVENT/gm) ?? []).length > maxEvents)
        return problem('ics_too_many_events', 422, instance, { limit: { max: maxEvents } })
      return response(200).json(mockIcsReport(params.id, text, query.get('dryRun') !== 'false'))
    }),
  ]
}
