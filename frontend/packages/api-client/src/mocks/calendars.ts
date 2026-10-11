import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import type { ErrorCode, ProblemDetails } from '../index'
import { mockProblem, mockUser } from './auth'
import { mockGroup, mockGroups, mockMemberGroup } from './groups'

type Calendar = components['schemas']['CalendarResponse']
type CalendarEvent = components['schemas']['EventResponse']
type EventTime = components['schemas']['EventTimeResponse']
type Grant = components['schemas']['GrantResponse']

/** A calendar of {@link mockGroup} (default role defaults); the mock user is its owner. */
export const mockCalendar: Calendar = {
  id: '0192f2c4-0000-7000-8000-000000000301',
  name: 'FC Lions – Club',
  description: 'Trainings and matches',
  color: '#4f46e5',
  defaultTimeZone: 'Europe/Berlin',
  owner: { type: 'group', id: mockGroup.id },
  myLevel: 'owner',
  groupRoleDefaults: { admin: 'manage', member: 'contribute', viewer: 'read' },
  creatorsManageOwnEvents: true,
  creatorsMayShareExternally: false,
  frozen: false,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
}

/** A personal calendar of the mock user. */
export const mockPersonalCalendar: Calendar = {
  ...mockCalendar,
  id: '0192f2c4-0000-7000-8000-000000000302',
  name: 'Mia',
  description: null,
  color: '#16a34a',
  owner: { type: 'user', id: mockUser.id },
  groupRoleDefaults: null,
}

/** A calendar of {@link mockMemberGroup}: the mock user (a member) may only read it. */
export const mockReadOnlyCalendar: Calendar = {
  ...mockCalendar,
  id: '0192f2c4-0000-7000-8000-000000000303',
  name: 'Book club',
  description: 'Monthly meetings',
  color: '#d97706',
  owner: { type: 'group', id: mockMemberGroup.id },
  myLevel: 'read',
  groupRoleDefaults: { admin: 'manage', member: 'read', viewer: 'read' },
}

/** Adam's personal calendar, shared with the mock user at `free_busy` (only busy times). */
export const mockBusyCalendar: Calendar = {
  ...mockCalendar,
  id: '0192f2c4-0000-7000-8000-000000000304',
  name: 'Adam',
  description: null,
  color: '#db2777',
  owner: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000002' },
  myLevel: 'free_busy',
  groupRoleDefaults: null,
}

/** A personal calendar of the mock user over the plan limit (frozen), shared with Adam. */
export const mockFrozenCalendar: Calendar = {
  ...mockPersonalCalendar,
  id: '0192f2c4-0000-7000-8000-000000000305',
  name: 'Old team',
  color: '#64748b',
  frozen: true,
}

/** A grant on {@link mockCalendar}: a user outside the group may edit it. */
export const mockGrant: Grant = {
  id: '0192f2c4-0000-7000-8000-000000000601',
  calendarId: mockCalendar.id,
  principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000005', minRole: null },
  principalName: 'Eve',
  level: 'edit',
  createdBy: mockUser.id,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  etag: '"grant-0601-1"',
}

/** Creating a calendar with this name answers `402 plan_limit_reached`. */
export const mockPlanLimitName = 'Over the limit'

/** A calendar the mock user cannot see; one of its events is shared with them ("Shared with me"). */
export const mockHiddenCalendarId = '0192f2c4-0000-7000-8000-000000000399'

/** A timed event in {@link mockCalendar}, created by the mock user (creator floor: manage). */
export const mockEvent: CalendarEvent = {
  id: '0192f2c4-0000-7000-8000-000000000401',
  calendarId: mockCalendar.id,
  uid: '0192f2c4-0000-7000-8000-000000000401@scalenderplus',
  title: 'Training',
  description: 'Bring shoes',
  location: 'Pitch 2',
  status: 'confirmed',
  categories: ['sport'],
  start: {
    dateTime: '2026-11-02T18:00:00',
    timeZone: 'Europe/Berlin',
    utc: '2026-11-02T17:00:00Z',
  },
  end: { dateTime: '2026-11-02T20:00:00', timeZone: 'Europe/Berlin', utc: '2026-11-02T19:00:00Z' },
  allDay: false,
  transparency: 'opaque',
  hasOverrides: false,
  sequence: 0,
  createdBy: { id: mockUser.id, displayName: 'Mia' },
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  myLevel: 'manage',
}

/** An all-day event the mock user only sees as busy (free_busy projection: times only, title null). */
export const mockBusyEvent: CalendarEvent = {
  id: '0192f2c4-0000-7000-8000-000000000402',
  calendarId: mockBusyCalendar.id,
  title: null,
  start: { date: '2026-11-04' },
  end: { date: '2026-11-05' },
  allDay: true,
  transparency: 'opaque',
  myLevel: 'free_busy',
}

/** A weekly series of the mock user in {@link mockCalendar} (Tuesdays 18:00–19:30 Berlin, 8 occurrences). */
export const mockSeries: CalendarEvent = {
  ...mockEvent,
  id: '0192f2c4-0000-7000-8000-000000000403',
  uid: '0192f2c4-0000-7000-8000-000000000403@scalenderplus',
  title: 'Weekly practice',
  categories: [],
  start: {
    dateTime: '2026-11-03T18:00:00',
    timeZone: 'Europe/Berlin',
    utc: '2026-11-03T17:00:00Z',
  },
  end: { dateTime: '2026-11-03T19:30:00', timeZone: 'Europe/Berlin', utc: '2026-11-03T18:30:00Z' },
  recurrence: { rrule: 'FREQ=WEEKLY;COUNT=8' },
}

const day = 24 * 60 * 60 * 1000
const zone = 'Europe/Berlin'

/** The UTC offset of {@link zone} at `utc` in milliseconds. */
function offsetAt(utc: number): number {
  const name = new Intl.DateTimeFormat('en-US', { timeZone: zone, timeZoneName: 'longOffset' })
    .formatToParts(new Date(utc))
    .find((p) => p.type === 'timeZoneName')?.value
  const match = /GMT([+-])(\d{2}):(\d{2})/.exec(name ?? '')
  if (!match) return 0
  const sign = match[1] === '-' ? -1 : 1
  return sign * (Number(match[2]) * 60 + Number(match[3])) * 60_000
}

/** A Berlin wall-clock time (`date` + `HH:MM`) as an api time value. */
function at(date: string, time: string): EventTime {
  const local = Date.parse(`${date}T${time}:00Z`)
  const utc = local - offsetAt(local - offsetAt(local))
  return {
    dateTime: `${date}T${time}:00`,
    timeZone: zone,
    utc: new Date(utc).toISOString().replace('.000Z', 'Z'),
  }
}

const isoDate = (ms: number) => new Date(ms).toISOString().slice(0, 10)

/** Monday of the current week in {@link zone}, the anchor of the seed so mock mode always looks alive. */
function currentMonday(now = Date.now()): number {
  const today = Date.parse(new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(now))
  const weekday = (new Date(today).getUTCDay() + 6) % 7
  return today - weekday * day
}

interface SeriesRecord {
  master: CalendarEvent
  /** Days between occurrences and their count (the mock knows simple weekly rules only). */
  everyDays: number
  count: number
  /** Recurrence ids (UTC) of cancelled occurrences. */
  cancelled: string[]
}

export interface MockCalendarsState {
  calendars: Calendar[]
  /** Personal overlays by calendar id (`version` makes the ETag). */
  prefs: Record<string, { hidden: boolean; color: string | null; version: number }>
  events: CalendarEvent[]
  series: SeriesRecord[]
  /** Window responses are cut after this many items with `truncated: true` (the api: 5,000). */
  truncateAt: number
  /** Grants of every calendar (`etag` changes with each update). */
  grants: Grant[]
  /** Versions of the calendars (their `ETag`). */
  versions: Record<string, number>
  /** `revokeEventShares` of the last grant or role-default change (default true). */
  lastRevokeEventShares: boolean | null
}

function seed(): MockCalendarsState {
  const monday = currentMonday()
  const d = (offset: number) => isoDate(monday + offset * day)
  let n = 0
  const id = () => `0192f2c4-0000-7000-8000-0000000005${String(++n).padStart(2, '0')}`
  const event = (fields: Partial<CalendarEvent> & Pick<CalendarEvent, 'start' | 'end'>) => {
    const eventId = id()
    return {
      ...mockEvent,
      id: eventId,
      uid: `${eventId}@scalenderplus`,
      description: null,
      location: null,
      categories: [],
      etag: `"${eventId.slice(-4)}-1"`,
      ...fields,
    } satisfies CalendarEvent
  }
  const busy = (calendarId: string, start: EventTime, end: EventTime): CalendarEvent => ({
    id: id(),
    calendarId,
    title: null,
    start,
    end,
    allDay: Boolean(start.date),
    transparency: 'opaque',
    myLevel: 'free_busy',
  })

  const practice: CalendarEvent = event({
    title: 'Team practice',
    description: 'Warm-up, passing drills and a short game. Bring water and both kits.',
    location: 'Sports park, pitch 2',
    start: at(d(-14 + 1), '18:00'),
    end: at(d(-14 + 1), '19:30'),
    recurrence: { rrule: 'FREQ=WEEKLY;COUNT=12' },
  })

  const events: CalendarEvent[] = [
    mockEvent,
    mockBusyEvent,
    event({
      title: 'Lunch with Max',
      calendarId: mockPersonalCalendar.id,
      location: 'Café Lindner',
      start: at(d(0), '12:30'),
      end: at(d(0), '13:30'),
    }),
    event({
      title: 'Summer camp planning',
      description: 'Dates, budget and who drives the minibus.',
      start: { date: d(2) },
      end: { date: d(4) },
      allDay: true,
      myLevel: 'edit',
      createdBy: { id: '0192f2c4-0000-7000-8000-000000000002', displayName: 'Adam' },
    }),
    busy(mockBusyCalendar.id, at(d(2), '09:00'), at(d(2), '11:30')),
    event({
      title: 'Quick call with the coach',
      calendarId: mockPersonalCalendar.id,
      start: at(d(1), '15:00'),
      end: at(d(1), '15:30'),
    }),
    event({
      title: 'Dentist',
      calendarId: mockPersonalCalendar.id,
      location: 'Dr. Weber, Hauptstraße 12',
      start: at(d(3), '08:30'),
      end: at(d(3), '09:15'),
    }),
    event({
      title: 'Parents’ evening',
      calendarId: mockHiddenCalendarId,
      description: 'Class 4b, room 2.11.',
      location: 'Primary school',
      start: at(d(3), '19:00'),
      end: at(d(3), '20:30'),
      myLevel: 'read',
      sharedWithMe: true,
      createdBy: { id: '0192f2c4-0000-7000-8000-000000000009', displayName: 'Ms Novak' },
    }),
    event({
      title: 'Book club: “The Overstory”',
      calendarId: mockReadOnlyCalendar.id,
      description: 'Chapters 1–8. Snacks: Lena.',
      location: 'Library, reading room',
      start: at(d(4), '19:30'),
      end: at(d(4), '21:30'),
      myLevel: 'read',
      createdBy: { id: '0192f2c4-0000-7000-8000-000000000006', displayName: 'Lena' },
    }),
    busy(mockBusyCalendar.id, at(d(4), '14:00'), at(d(4), '16:00')),
    event({
      title: 'League match vs. SV Nord',
      description: 'Meet 9:15 at the clubhouse.',
      location: 'Stadium Nord',
      start: at(d(5), '10:00'),
      end: at(d(5), '12:00'),
    }),
    event({
      title: 'Board meeting',
      location: 'Clubhouse',
      start: at(d(8), '19:00'),
      end: at(d(8), '20:30'),
      myLevel: 'read',
      createdBy: { id: '0192f2c4-0000-7000-8000-000000000002', displayName: 'Adam' },
    }),
    busy(mockBusyCalendar.id, { date: d(9) }, { date: d(10) }),
    event({
      title: 'Mum’s birthday',
      calendarId: mockPersonalCalendar.id,
      start: { date: d(11) },
      end: { date: d(12) },
      allDay: true,
    }),
    event({
      title: 'Season kick-off party',
      location: 'Clubhouse',
      start: at(d(-5), '18:00'),
      end: at(d(-5), '23:00'),
    }),
  ]

  return {
    calendars: [
      mockCalendar,
      mockPersonalCalendar,
      mockReadOnlyCalendar,
      mockBusyCalendar,
      mockFrozenCalendar,
    ],
    grants: [
      { ...mockGrant },
      {
        ...mockGrant,
        id: '0192f2c4-0000-7000-8000-000000000602',
        calendarId: mockFrozenCalendar.id,
        principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000002', minRole: null },
        principalName: 'Adam',
        level: 'edit',
        etag: '"grant-0602-1"',
      },
    ],
    versions: {},
    lastRevokeEventShares: null,
    prefs: {},
    events,
    series: [
      { master: mockSeries, everyDays: 7, count: 8, cancelled: [] },
      {
        master: practice,
        everyDays: 7,
        count: 12,
        // One cancelled occurrence (two weeks ahead) shows that exceptions are applied.
        cancelled: [at(d(15), '18:00').utc ?? ''],
      },
    ],
    truncateAt: 5000,
  }
}

/** Mutable calendars, overlays and events of the mocks (reset per test with {@link resetMockCalendars}). */
export const mockCalendars: MockCalendarsState = seed()

export function resetMockCalendars(): void {
  Object.assign(mockCalendars, seed())
}

/** Shifts a time value by whole days (wall clock and UTC alike: no DST handling in the mock). */
function plusDays(value: EventTime, days: number): EventTime {
  if (value.date) return { date: isoDate(Date.parse(value.date) + days * day) }
  const date = isoDate(Date.parse(`${value.dateTime}Z`) + days * day)
  return at(date, (value.dateTime ?? '').slice(11, 16))
}

/** The occurrences of a series (cancelled ones left out), as `expand=occurrences` returns them. */
export function mockOccurrences(series: SeriesRecord): CalendarEvent[] {
  return Array.from({ length: series.count }, (_, i) => {
    const start = plusDays(series.master.start, i * series.everyDays)
    const recurrenceId = start.utc ?? start.date ?? ''
    return {
      ...series.master,
      start,
      end: plusDays(series.master.end, i * series.everyDays),
      occurrenceId: `${series.master.id}:${recurrenceId}`,
      recurrenceId,
      etag: `"series-${series.master.id.slice(-4)}"`,
    }
  }).filter((o) => !series.cancelled.includes(o.recurrenceId))
}

const rank = ['none', 'free_busy', 'read', 'contribute', 'edit', 'manage', 'owner']
const levelRank = (level: string) => rank.indexOf(level)

/** The `ETag` of a calendar (changes with every update). */
export function mockCalendarEtag(id: string): string {
  return `"cal-${id.slice(-4)}-${mockCalendars.versions[id] ?? 1}"`
}

/** Display name of a user or group the mocks know (members of the mock groups, the groups). */
function principalName(type: string, id: string): string | null {
  if (type === 'group') return mockGroups.groups.find((g) => g.id === id)?.name ?? null
  for (const group of mockGroups.groups) {
    const member = group.members.find((m) => m.userId === id)
    if (member) return member.displayName
  }
  return mockCalendars.grants.find((g) => g.principal.id === id)?.principalName ?? null
}

const prefsEtag = (calendarId: string, version: number) =>
  `"prefs-${calendarId.slice(-4)}-${version}"`

/** Stateful handlers for `/calendars` (list, create), `/events` (window) and the personal overlay. */
export function calendarHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  const visible = (id: string) => mockCalendars.calendars.find((c) => c.id === id)
  /** A problem response (a plain `Response`: valid for every route of the typed handlers). */
  const problem = (
    code: ErrorCode,
    status: number,
    instance: string,
    extras: Partial<ProblemDetails> = {},
  ): Response =>
    new Response(JSON.stringify(mockProblem(code, status, instance, extras)), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    })
  /** `If-Match` of a guarded change: missing → 428, stale → 412, else null. */
  const checkIfMatch = (request: Request, etag: string, instance: string) => {
    const ifMatch = request.headers.get('If-Match')
    if (!ifMatch) return problem('precondition_required', 428, instance)
    if (ifMatch !== '*' && ifMatch !== etag) return problem('precondition_failed', 412, instance)
    return null
  }
  const prefsOf = (calendarId: string) => {
    const p = mockCalendars.prefs[calendarId]
    return {
      calendarId,
      hidden: p?.hidden ?? false,
      color: p?.color ?? null,
      etag: prefsEtag(calendarId, p?.version ?? 0),
    }
  }

  return [
    http.get('/api/v1/calendars', ({ response }) =>
      response(200).json({ items: mockCalendars.calendars, nextCursor: null }),
    ),
    http.post('/api/v1/calendars', async ({ request, response }) => {
      const body = await request.json()
      const groupId = body.groupId ?? null
      if (body.name.trim() === mockPlanLimitName) {
        return problem('plan_limit_reached', 402, '/api/v1/calendars')
      }
      const calendar: Calendar = {
        ...mockCalendar,
        id: crypto.randomUUID(),
        name: body.name,
        description: body.description ?? null,
        color: body.color?.toLowerCase() ?? '#4f46e5',
        defaultTimeZone: body.defaultTimeZone,
        owner: groupId ? { type: 'group', id: groupId } : { type: 'user', id: mockUser.id },
        groupRoleDefaults: groupId
          ? {
              admin: body.groupRoleDefaults?.admin ?? 'manage',
              member: body.groupRoleDefaults?.member ?? 'contribute',
              viewer: body.groupRoleDefaults?.viewer ?? 'read',
            }
          : null,
        creatorsManageOwnEvents: body.creatorsManageOwnEvents ?? true,
        creatorsMayShareExternally: body.creatorsMayShareExternally ?? false,
        frozen: false,
      }
      mockCalendars.calendars.push(calendar)
      return response(201).json(calendar, { headers: { ETag: mockCalendarEtag(calendar.id) } })
    }),

    http.get('/api/v1/calendars/{id}', ({ params, response }) => {
      const calendar = visible(params.id)
      if (!calendar) return problem('not_found', 404, `/api/v1/calendars/${params.id}`)
      return response(200).json(calendar, { headers: { ETag: mockCalendarEtag(calendar.id) } })
    }),
    http.patch('/api/v1/calendars/{id}', async ({ params, query, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}`
      const calendar = visible(params.id)
      if (!calendar) return problem('not_found', 404, instance)
      if (levelRank(calendar.myLevel) < levelRank('manage')) {
        return problem('insufficient_permission', 403, instance)
      }
      const precondition = checkIfMatch(request, mockCalendarEtag(calendar.id), instance)
      if (precondition) return precondition
      if (calendar.frozen) return problem('calendar_frozen', 409, instance)
      const patch = (await request.json()) as components['schemas']['UpdateCalendarRequest']
      const defaults = calendar.groupRoleDefaults
      if (
        patch.groupRoleDefaults?.admin &&
        calendar.myLevel === 'manage' &&
        levelRank(patch.groupRoleDefaults.admin) < levelRank('manage')
      ) {
        return problem('permission_self_lockout', 409, instance)
      }
      const updated: Calendar = {
        ...calendar,
        name: patch.name ?? calendar.name,
        description: patch.description === '' ? null : (patch.description ?? calendar.description),
        color: patch.color?.toLowerCase() ?? calendar.color,
        defaultTimeZone: patch.defaultTimeZone ?? calendar.defaultTimeZone,
        creatorsManageOwnEvents: patch.creatorsManageOwnEvents ?? calendar.creatorsManageOwnEvents,
        creatorsMayShareExternally:
          patch.creatorsMayShareExternally ?? calendar.creatorsMayShareExternally,
        groupRoleDefaults: defaults && {
          admin: patch.groupRoleDefaults?.admin ?? defaults.admin,
          member: patch.groupRoleDefaults?.member ?? defaults.member,
          viewer: patch.groupRoleDefaults?.viewer ?? defaults.viewer,
        },
        updatedAt: new Date().toISOString(),
      }
      mockCalendars.calendars = mockCalendars.calendars.map((c) =>
        c.id === calendar.id ? updated : c,
      )
      mockCalendars.versions[calendar.id] = (mockCalendars.versions[calendar.id] ?? 1) + 1
      mockCalendars.lastRevokeEventShares = query.get('revokeEventShares') !== 'false'
      return response(200).json(updated, { headers: { ETag: mockCalendarEtag(calendar.id) } })
    }),
    http.delete('/api/v1/calendars/{id}', ({ params, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}`
      const calendar = visible(params.id)
      if (!calendar) return problem('not_found', 404, instance)
      if (calendar.myLevel !== 'owner') return problem('insufficient_permission', 403, instance)
      const precondition = checkIfMatch(request, mockCalendarEtag(calendar.id), instance)
      if (precondition) return precondition
      mockCalendars.calendars = mockCalendars.calendars.filter((c) => c.id !== calendar.id)
      mockCalendars.grants = mockCalendars.grants.filter((g) => g.calendarId !== calendar.id)
      mockCalendars.events = mockCalendars.events.filter((e) => e.calendarId !== calendar.id)
      mockCalendars.series = mockCalendars.series.filter((r) => r.master.calendarId !== calendar.id)
      return response(204).empty()
    }),

    http.get('/api/v1/calendars/{id}/grants', ({ params, response }) => {
      const instance = `/api/v1/calendars/${params.id}/grants`
      const calendar = visible(params.id)
      if (!calendar) return problem('not_found', 404, instance)
      if (levelRank(calendar.myLevel) < levelRank('manage')) {
        return problem('insufficient_permission', 403, instance)
      }
      return response(200).json({
        items: mockCalendars.grants.filter((g) => g.calendarId === calendar.id),
        nextCursor: null,
      })
    }),
    http.post('/api/v1/calendars/{id}/grants', async ({ params, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}/grants`
      const calendar = visible(params.id)
      if (!calendar) return problem('not_found', 404, instance)
      if (levelRank(calendar.myLevel) < levelRank('manage')) {
        return problem('insufficient_permission', 403, instance)
      }
      if (calendar.frozen) return problem('calendar_frozen', 409, instance)
      const body = await request.json()
      const name = principalName(body.principal.type, body.principal.id)
      if (!name) {
        return problem('validation_failed', 400, instance, {
          errors: { principal: ['Not selectable.'] },
        })
      }
      if (
        mockCalendars.grants.some(
          (g) => g.calendarId === calendar.id && g.principal.id === body.principal.id,
        )
      ) {
        return problem('conflict', 409, instance)
      }
      const id = crypto.randomUUID()
      const grant: Grant = {
        id,
        calendarId: calendar.id,
        principal: {
          type: body.principal.type,
          id: body.principal.id,
          minRole: body.principal.type === 'group' ? (body.principal.minRole ?? 'viewer') : null,
        },
        principalName: name,
        level: body.level,
        createdBy: mockUser.id,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        etag: `"grant-${id.slice(-4)}-1"`,
      }
      mockCalendars.grants.push(grant)
      return response(201).json(grant, { headers: { ETag: grant.etag } })
    }),
    http.patch(
      '/api/v1/calendars/{id}/grants/{grantId}',
      async ({ params, query, request, response }) => {
        const instance = `/api/v1/calendars/${params.id}/grants/${params.grantId}`
        const calendar = visible(params.id)
        const grant = mockCalendars.grants.find(
          (g) => g.id === params.grantId && g.calendarId === params.id,
        )
        if (!calendar || !grant) return problem('not_found', 404, instance)
        const precondition = checkIfMatch(request, grant.etag, instance)
        if (precondition) return precondition
        const level = (await request.json()).level ?? grant.level
        if (calendar.frozen && levelRank(level) > levelRank(grant.level)) {
          return problem('calendar_frozen', 409, instance)
        }
        const version = Number(/-(\d+)"$/.exec(grant.etag)?.[1] ?? '1') + 1
        const updated: Grant = {
          ...grant,
          level,
          updatedAt: new Date().toISOString(),
          etag: `"grant-${grant.id.slice(-4)}-${version}"`,
        }
        mockCalendars.grants = mockCalendars.grants.map((g) => (g.id === grant.id ? updated : g))
        mockCalendars.lastRevokeEventShares = query.get('revokeEventShares') !== 'false'
        return response(200).json(updated, { headers: { ETag: updated.etag } })
      },
    ),
    http.delete(
      '/api/v1/calendars/{id}/grants/{grantId}',
      ({ params, query, request, response }) => {
        const instance = `/api/v1/calendars/${params.id}/grants/${params.grantId}`
        const grant = mockCalendars.grants.find(
          (g) => g.id === params.grantId && g.calendarId === params.id,
        )
        if (!visible(params.id) || !grant) return problem('not_found', 404, instance)
        const precondition = checkIfMatch(request, grant.etag, instance)
        if (precondition) return precondition
        mockCalendars.grants = mockCalendars.grants.filter((g) => g.id !== grant.id)
        mockCalendars.lastRevokeEventShares = query.get('revokeEventShares') !== 'false'
        return response(204).empty()
      },
    ),

    http.get('/api/v1/me/calendar-prefs', ({ response }) =>
      response(200).json({
        items: Object.keys(mockCalendars.prefs).map(prefsOf),
      }),
    ),
    http.get('/api/v1/calendars/{id}/prefs', ({ params, response }) => {
      const instance = `/api/v1/calendars/${params.id}/prefs`
      if (!visible(params.id)) {
        return response('default').json(mockProblem('not_found', 404, instance), { status: 404 })
      }
      const prefs = prefsOf(params.id)
      return response(200).json(prefs, { headers: { ETag: prefs.etag } })
    }),
    http.put('/api/v1/calendars/{id}/prefs', async ({ params, request, response }) => {
      const instance = `/api/v1/calendars/${params.id}/prefs`
      if (!visible(params.id)) {
        return response('default').json(mockProblem('not_found', 404, instance), { status: 404 })
      }
      const ifMatch = request.headers.get('If-Match')
      const current = prefsOf(params.id)
      if (!ifMatch) {
        return response('default').json(mockProblem('precondition_required', 428, instance), {
          status: 428,
        })
      }
      if (ifMatch !== '*' && ifMatch !== current.etag) {
        return response('default').json(mockProblem('precondition_failed', 412, instance), {
          status: 412,
        })
      }
      const body = await request.json()
      const color = body.color ?? null
      if (color !== null && !/^#[0-9a-fA-F]{6}$/.test(color)) {
        return response('default').json(
          mockProblem('validation_failed', 400, instance, { errors: { color: ['Invalid.'] } }),
          { status: 400 },
        )
      }
      const version = (mockCalendars.prefs[params.id]?.version ?? 0) + 1
      mockCalendars.prefs[params.id] = {
        hidden: body.hidden ?? false,
        color: color?.toLowerCase() ?? null,
        version,
      }
      const prefs = prefsOf(params.id)
      return response(200).json(prefs, { headers: { ETag: prefs.etag } })
    }),

    http.get('/api/v1/events', ({ query, response }) => {
      const from = query.get('from') ?? ''
      const to = query.get('to') ?? ''
      const calendarIds = query.getAll('calendarIds')
      const expand = query.get('expand') === 'occurrences'
      // Series as their occurrences (expand=occurrences) or their master.
      const candidates = [
        ...mockCalendars.events,
        ...mockCalendars.series.flatMap((s) => (expand ? mockOccurrences(s) : [s.master])),
      ]
      const fromDate = from.slice(0, 10)
      const toDate = to.slice(0, 10)
      // Overlap on the UTC (timed) or date (all-day) bounds; the real api places all-day events per zone.
      const items = candidates
        .filter(
          (e) =>
            (calendarIds.length === 0 || calendarIds.includes(e.calendarId)) &&
            (e.start.utc ? e.start.utc < to : (e.start.date ?? '') < toDate) &&
            (e.end.utc ? e.end.utc > from : (e.end.date ?? '') > fromDate),
        )
        .sort((a, b) =>
          (a.start.utc ?? `${a.start.date}T00:00:00Z`).localeCompare(
            b.start.utc ?? `${b.start.date}T00:00:00Z`,
          ),
        )
      const truncated = items.length > mockCalendars.truncateAt
      return response(200).json({
        items: items.slice(0, mockCalendars.truncateAt),
        truncated,
      })
    }),
  ]
}
