import type { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import type { ErrorCode } from '../index'
import { mockProblem, mockUser } from './auth'
import { mockCalendar, mockCalendars, mockEvent } from './calendars'
import { mockGroup, mockGroups } from './groups'

type CalendarEvent = components['schemas']['EventResponse']
type Override = components['schemas']['OverrideResponse']
type Principal = components['schemas']['PrincipalResponse']
type Step = components['schemas']['ExplainStepResponse']

/**
 * Stateful event overrides and the access explainer (api.md "Event overrides", "Access
 * explainer") with a small copy of the engine (permissions.md §4.2). "Training" ({@link mockEvent})
 * is the mock user's own event through the creator floor: sharing it with someone outside
 * FC Lions (e.g. Olga from the book club) is refused as external sharing. More than
 * {@link mockOverrideLimit} entries hit the plan limit; the frozen calendar takes removals and
 * lowerings only.
 */
export const mockOverrideLimit = 5

const calendarRank = ['none', 'free_busy', 'read', 'contribute', 'edit', 'manage', 'owner']
const eventRank = ['none', 'free_busy', 'read', 'edit', 'manage']
const roleRank = ['viewer', 'member', 'admin', 'owner']
const tiers: Record<string, number> = { user: 4, group: 3, anonymous: 2, everyone: 1 }

const atLeast = (level: string, minimum: string) =>
  calendarRank.indexOf(level) >= calendarRank.indexOf(minimum)

function problem(code: ErrorCode, status: number, instance: string, extras: object = {}) {
  return new Response(JSON.stringify({ ...mockProblem(code, status, instance), ...extras }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

const json = (body: unknown, etag?: string) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json', ...(etag ? { ETag: etag } : {}) },
  })

const findEvent = (id: string): CalendarEvent | undefined =>
  mockCalendars.events.find((e) => e.id === id) ??
  mockCalendars.series.find((s) => s.master.id === id)?.master

const etagOf = (eventId: string) =>
  `"ov-${eventId.slice(-4)}-${mockCalendars.overrides[eventId]?.version ?? 0}"`

const keyOf = (p: Principal) =>
  p.type === 'user' || p.type === 'group' ? `${p.type}:${p.id}` : p.type

/** The groups the mock user is in, with their members. */
const myGroups = () =>
  mockGroups.groups.filter((g) => g.members.some((m) => m.userId === mockUser.id))

function nameOf(p: Principal): string | undefined {
  if (p.type === 'group') return mockGroups.groups.find((g) => g.id === p.id)?.name
  if (p.type !== 'user') return undefined
  for (const g of mockGroups.groups) {
    const m = g.members.find((x) => x.userId === p.id)
    if (m) return m.displayName
  }
  return undefined
}

const roleIn = (groupId: string | null | undefined, userId: string) =>
  mockGroups.groups.find((g) => g.id === groupId)?.members.find((m) => m.userId === userId)?.role

/** The calendar level of a user and the steps that led there (role defaults of FC Lions only). */
function calendarLevel(userId: string, calendarId: string): { level: string; steps: Step[] } {
  const calendar = mockCalendars.calendars.find((c) => c.id === calendarId)
  if (userId === mockUser.id) {
    if (!calendar)
      return { level: 'none', steps: [{ kind: 'calendar_result', calendarLevel: 'none' }] }
    if (calendar.myLevel === 'owner') {
      return {
        level: 'owner',
        steps: [
          {
            kind: 'calendar_owner',
            calendarLevel: 'owner',
            principal: { type: 'user', id: userId },
          },
        ],
      }
    }
    return {
      level: calendar.myLevel,
      steps: [{ kind: 'calendar_result', calendarLevel: calendar.myLevel }],
    }
  }
  if (calendarId === mockCalendar.id) {
    const role = roleIn(mockGroup.id, userId)
    if (role === 'owner')
      return { level: 'owner', steps: [{ kind: 'calendar_owner', calendarLevel: 'owner' }] }
    const defaults = mockCalendar.groupRoleDefaults as Record<string, string> | null | undefined
    const level = role ? (defaults?.[role] ?? 'none') : 'none'
    const steps: Step[] = role
      ? [
          {
            kind: 'group_role_default',
            principal: { type: 'group', id: mockGroup.id },
            principalName: mockGroup.name,
            role,
            calendarLevel: level,
          },
        ]
      : []
    return { level, steps: [...steps, { kind: 'calendar_result', calendarLevel: level }] }
  }
  return { level: 'none', steps: [{ kind: 'calendar_result', calendarLevel: 'none' }] }
}

function implied(level: string): string {
  if (level === 'owner' || level === 'manage') return 'manage'
  if (level === 'contribute') return 'read'
  return level
}

function matches(p: Principal, userId: string, lc: string): boolean {
  if (p.type === 'user') return p.id === userId
  if (p.type === 'group') {
    const role = roleIn(p.id, userId)
    return !!role && roleRank.indexOf(role) >= roleRank.indexOf(p.minRole ?? 'viewer')
  }
  if (p.type === 'everyone') return atLeast(lc, 'free_busy')
  return false
}

/** `Le(U, E)` with its trace (permissions.md §4.2). */
export function mockExplain(
  event: CalendarEvent,
  userId: string,
): { level: string; calendarLevel: string; steps: Step[] } {
  // The mock user's own "Training": the creator floor in a calendar where members contribute.
  if (userId === mockUser.id && event.id === mockEvent.id) {
    return {
      level: 'manage',
      calendarLevel: 'contribute',
      steps: [
        {
          kind: 'group_role_default',
          principal: { type: 'group', id: mockGroup.id },
          principalName: mockGroup.name,
          role: 'member',
          calendarLevel: 'contribute',
        },
        { kind: 'calendar_result', calendarLevel: 'contribute' },
        { kind: 'creator_floor', eventLevel: 'manage', decisive: true },
        { kind: 'result', eventLevel: 'manage' },
      ],
    }
  }
  const lc = calendarLevel(userId, event.calendarId)
  const steps = [...lc.steps]
  const done = (level: string) => ({
    level,
    calendarLevel: lc.level,
    steps: [...steps, { kind: 'result', eventLevel: level }],
  })
  if (atLeast(lc.level, 'manage')) {
    steps.push({ kind: 'manager_floor', eventLevel: 'manage', decisive: true })
    return done('manage')
  }
  if (event.createdBy?.id === userId && atLeast(lc.level, 'contribute')) {
    steps.push({ kind: 'creator_floor', eventLevel: 'manage', decisive: true })
    return done('manage')
  }
  // "Shared with me": the mock user's level comes from the event as the list reports it.
  const base = userId === mockUser.id && event.sharedWithMe ? 'none' : implied(lc.level)
  steps.push({ kind: 'base_level', eventLevel: base })
  const items = mockCalendars.overrides[event.id]?.items ?? []
  const matching = items.filter((o) => matches(o.principal, userId, lc.level))
  if (matching.length === 0) {
    steps.push({ kind: 'no_matching_override' })
    steps[steps.findIndex((s) => s.kind === 'base_level')]!.decisive = true
    return done(base)
  }
  const top = Math.max(...matching.map((o) => tiers[o.principal.type] ?? 0))
  for (const o of matching) {
    steps.push({
      kind: 'override_matched',
      principal: o.principal,
      principalName: o.principalName,
      eventLevel: o.level,
      tier: tiers[o.principal.type],
      applied: tiers[o.principal.type] === top,
    })
  }
  const winners = matching.filter((o) => tiers[o.principal.type] === top)
  let level = winners
    .map((o) => o.level)
    .sort((a, b) => eventRank.indexOf(b) - eventRank.indexOf(a))[0]!
  const winner = winners.find((o) => o.level === level)!
  steps.push({
    kind: 'override_applied',
    principal: winner.principal,
    principalName: winner.principalName,
    eventLevel: level,
    tier: top,
    decisive: true,
  })
  if (top <= tiers.anonymous! && eventRank.indexOf(level) > eventRank.indexOf(base)) {
    level = base
    steps.push({ kind: 'restrict_only', eventLevel: level })
  }
  return done(level)
}

interface Violation {
  principal: Principal
  level: string
  reason: string
}

export function overrideHandlers(http: ReturnType<typeof createOpenApiHttp<paths>>) {
  return [
    http.get('/api/v1/events/{id}/overrides', ({ params }) => {
      const instance = `/api/v1/events/${params.id}/overrides`
      const event = findEvent(params.id)
      if (!event || event.myLevel === 'none') return problem('not_found', 404, instance)
      if (event.myLevel !== 'manage') {
        return problem('insufficient_permission', 403, instance, {
          required: 'manage',
          actual: event.myLevel,
        })
      }
      const etag = etagOf(event.id)
      return json(
        { eventId: event.id, items: mockCalendars.overrides[event.id]?.items ?? [], etag },
        etag,
      )
    }),
    http.put('/api/v1/events/{id}/overrides', async ({ params, request }) => {
      const instance = `/api/v1/events/${params.id}/overrides`
      const event = findEvent(params.id)
      if (!event || event.myLevel === 'none') return problem('not_found', 404, instance)
      if (event.myLevel !== 'manage') return problem('insufficient_permission', 403, instance)
      const ifMatch = request.headers.get('If-Match')
      if (!ifMatch) return problem('precondition_required', 428, instance)
      if (ifMatch !== '*' && ifMatch !== etagOf(event.id)) {
        return problem('precondition_failed', 412, instance)
      }
      const body = await request.json()
      const wanted = (body.overrides ?? []).map((o) => ({
        principal: {
          type: o.principal.type,
          id: o.principal.id ?? null,
          minRole: o.principal.type === 'group' ? (o.principal.minRole ?? 'viewer') : null,
        },
        level: o.level,
      }))
      const before = new Map(
        (mockCalendars.overrides[event.id]?.items ?? []).map((o) => [keyOf(o.principal), o.level]),
      )
      const isNewOrRaised = (key: string, level: string) =>
        !before.has(key) || eventRank.indexOf(level) > eventRank.indexOf(before.get(key)!)

      const invalid: Violation[] = []
      const seen = new Set<string>()
      const lions = mockGroups.groups.find((g) => g.id === mockGroup.id)
      const selectableUsers = new Set(myGroups().flatMap((g) => g.members.map((m) => m.userId)))
      const external: Violation[] = []
      for (const o of wanted) {
        const key = keyOf(o.principal)
        if (seen.has(key)) invalid.push({ ...o, reason: 'duplicate_principal' })
        seen.add(key)
        if (!['none', 'free_busy', 'read', 'edit'].includes(o.level)) {
          invalid.push({ ...o, reason: 'level_too_high' })
        }
        if (!isNewOrRaised(key, o.level)) continue
        if (o.principal.type === 'group' && !myGroups().some((g) => g.id === o.principal.id)) {
          invalid.push({ ...o, reason: 'group_not_selectable' })
        }
        if (o.principal.type === 'user' && !selectableUsers.has(o.principal.id ?? '')) {
          invalid.push({ ...o, reason: 'user_not_selectable' })
        }
        // Training: creator floor only, no external sharing (Olga is not in FC Lions).
        if (
          event.id === mockEvent.id &&
          o.principal.type === 'user' &&
          o.level !== 'none' &&
          !lions?.members.some((m) => m.userId === o.principal.id)
        ) {
          external.push({ ...o, reason: 'external_sharing' })
        }
      }
      if (invalid.length) {
        return problem('override_invalid', 422, instance, {
          violations: invalid,
          errors: { overrides: invalid.map((v) => v.reason) },
        })
      }
      if (external.length) {
        return problem('external_sharing_not_allowed', 403, instance, { violations: external })
      }
      const calendar = mockCalendars.calendars.find((c) => c.id === event.calendarId)
      if (calendar?.frozen && wanted.some((o) => isNewOrRaised(keyOf(o.principal), o.level))) {
        return problem('calendar_frozen', 409, instance)
      }
      if (wanted.length > mockOverrideLimit && wanted.length > before.size) {
        return problem('plan_limit_reached', 402, instance, {
          limit: { key: 'overrides_per_event', max: mockOverrideLimit, used: before.size },
          plan: 'free',
        })
      }
      const items: Override[] = wanted.map((o) => ({
        principal: o.principal,
        principalName: nameOf(o.principal),
        level: o.level,
        createdBy: mockUser.id,
        createdAt: new Date().toISOString(),
      }))
      const version = (mockCalendars.overrides[event.id]?.version ?? 0) + 1
      mockCalendars.overrides[event.id] = { items, version }
      event.hasOverrides = items.length > 0
      const etag = etagOf(event.id)
      return json({ eventId: event.id, items, etag }, etag)
    }),
    http.get('/api/v1/events/{id}/access/explain', ({ params, query }) => {
      const instance = `/api/v1/events/${params.id}/access/explain`
      const event = findEvent(params.id)
      if (!event || event.myLevel === 'none') return problem('not_found', 404, instance)
      const userId = query.get('userId') ?? mockUser.id
      if (userId !== mockUser.id) {
        const calendar = mockCalendars.calendars.find((c) => c.id === event.calendarId)
        if (!calendar || !atLeast(calendar.myLevel, 'manage')) {
          return problem('insufficient_permission', 403, instance)
        }
      }
      const name =
        userId === mockUser.id ? mockUser.displayName : nameOf({ type: 'user', id: userId })
      if (!name) return problem('not_found', 404, instance)
      const result = mockExplain(event, userId)
      const level = userId === mockUser.id && event.sharedWithMe ? event.myLevel : result.level
      return json({
        eventId: event.id,
        calendarId: event.calendarId,
        user: { id: userId, displayName: name },
        calendarLevel: result.calendarLevel,
        level,
        steps: result.steps,
      })
    }),
  ]
}
