import { createOpenApiHttp } from 'openapi-msw'
import type { components, paths } from '../schema'
import { authHandlers, mockUser, sessionGuard } from './auth'
import {
  calendarHandlers,
  mockCalendar,
  mockCalendars,
  mockEvent,
  mockOccurrences,
  mockSeries,
} from './calendars'
import { groupHandlers, mockGroup } from './groups'

export {
  mockAuth,
  mockCredentials,
  mockProblem,
  mockUser,
  resetMockAuth,
  type MockAuthState,
} from './auth'
export {
  mockBusyCalendar,
  mockBusyEvent,
  mockCalendar,
  mockCalendars,
  mockEvent,
  mockHiddenCalendarId,
  mockPersonalCalendar,
  mockReadOnlyCalendar,
  mockSeries,
  resetMockCalendars,
  type MockCalendarsState,
} from './calendars'
export {
  mockGroup,
  mockGroups,
  mockInvite,
  mockInvitedGroup,
  mockInviteTokens,
  mockMemberGroup,
  mockMembers,
  resetMockGroups,
  type MockGroupsState,
} from './groups'

/**
 * MSW handlers typed against the generated `paths`: a contract change that the mocks don't follow
 * fails `typecheck`. Shared by the app's mock mode (`pnpm --filter app dev:mock`), Vitest and the
 * Playwright `mocked` project.
 */
export const http = createOpenApiHttp<paths>()

/** A time value of a request as the api answers it (no zone conversion: utc is left out). */
function eventTime(
  value: components['schemas']['EventTimeRequest'] | null | undefined,
  zone: string,
): components['schemas']['EventTimeResponse'] {
  return value?.date
    ? { date: value.date }
    : { dateTime: value?.dateTime ?? '', timeZone: value?.timeZone ?? zone }
}

type Grant = components['schemas']['GrantResponse']

/** A grant on {@link mockCalendar}: a user outside the group may edit it. */
export const mockGrant: Grant = {
  id: '0192f2c4-0000-7000-8000-000000000401',
  calendarId: mockCalendar.id,
  principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000005', minRole: null },
  principalName: 'Eve',
  level: 'edit',
  createdBy: mockUser.id,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  etag: '"grant-0401"',
}

type Override = components['schemas']['OverrideResponse']

/** The overrides of {@link mockEvent}: hidden from Vic, read-only for Eve outside the group. */
export const mockOverrides: Override[] = [
  {
    principal: { type: 'user', id: '0192f2c4-0000-7000-8000-000000000004' },
    principalName: 'Vic',
    level: 'none',
    createdBy: mockUser.id,
    createdAt: '2026-10-01T08:00:00Z',
  },
  {
    principal: { type: 'everyone' },
    level: 'free_busy',
    createdBy: mockUser.id,
    createdAt: '2026-10-01T08:00:00Z',
  },
]

export const handlers = [
  sessionGuard,
  http.get('/health/live', ({ response }) => response(200).json({ status: 'Healthy', checks: {} })),
  http.get('/health/ready', ({ response }) =>
    response(200).json({ status: 'Healthy', checks: { database: 'Healthy' } }),
  ),
  ...authHandlers(http),

  ...groupHandlers(http),

  ...calendarHandlers(http),

  http.get('/api/v1/calendars/{id}', ({ params, response }) => {
    const calendar = mockCalendars.calendars.find((c) => c.id === params.id)
    return calendar
      ? response(200).json(calendar)
      : response('default').json(notFound(`/api/v1/calendars/${params.id}`), { status: 404 })
  }),
  http.patch('/api/v1/calendars/{id}', async ({ params, request, response }) => {
    const calendar = mockCalendars.calendars.find((c) => c.id === params.id)
    if (!calendar) {
      return response('default').json(notFound(`/api/v1/calendars/${params.id}`), { status: 404 })
    }
    const patch = (await request.json()) as components['schemas']['UpdateCalendarRequest']
    const defaults = calendar.groupRoleDefaults
    return response(200).json({
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
    })
  }),
  http.delete('/api/v1/calendars/{id}', ({ response }) => response(204).empty()),
  http.get('/api/v1/calendars/{id}/grants', ({ params, response }) =>
    response(200).json({
      items: params.id === mockCalendar.id ? [mockGrant] : [],
      nextCursor: null,
    }),
  ),
  http.post('/api/v1/calendars/{id}/grants', async ({ params, request, response }) => {
    const body = await request.json()
    const minRole = body.principal.type === 'group' ? (body.principal.minRole ?? 'viewer') : null
    return response(201).json({
      ...mockGrant,
      id: crypto.randomUUID(),
      calendarId: params.id,
      principal: { type: body.principal.type, id: body.principal.id, minRole },
      principalName: body.principal.type === 'group' ? mockGroup.name : 'Max',
      level: body.level,
    })
  }),
  http.patch('/api/v1/calendars/{id}/grants/{grantId}', async ({ params, request, response }) => {
    if (params.grantId !== mockGrant.id) {
      return response('default').json(
        notFound(`/api/v1/calendars/${params.id}/grants/${params.grantId}`),
        { status: 404 },
      )
    }
    const { level } = await request.json()
    return response(200).json({ ...mockGrant, level: level ?? mockGrant.level })
  }),
  http.delete('/api/v1/calendars/{id}/grants/{grantId}', ({ response }) => response(204).empty()),

  http.post('/api/v1/events', async ({ request, response }) => {
    const body = await request.json()
    const zone = body.start?.timeZone ?? mockCalendar.defaultTimeZone
    const id = crypto.randomUUID()
    return response(201).json({
      ...mockEvent,
      id,
      uid: body.uid ?? `${id}@scalenderplus`,
      calendarId: body.calendarId ?? mockCalendar.id,
      title: body.title ?? '',
      description: body.description ?? null,
      location: body.location ?? null,
      url: body.url ?? null,
      status: body.status ?? 'confirmed',
      transparency: body.transparency ?? 'opaque',
      color: body.color?.toLowerCase() ?? null,
      categories: body.categories ?? [],
      start: eventTime(body.start, zone),
      end: eventTime(body.end, zone),
      allDay: Boolean(body.start?.date),
    })
  }),
  http.get('/api/v1/events/{id}', ({ params, response }) => {
    const event = mockCalendars.events.find((e) => e.id === params.id)
    return event
      ? response(200).json(event)
      : response('default').json(notFound(`/api/v1/events/${params.id}`), { status: 404 })
  }),
  http.patch('/api/v1/events/{id}', async ({ params, request, response }) => {
    const event = mockCalendars.events.find((e) => e.id === params.id)
    if (!event) {
      return response('default').json(notFound(`/api/v1/events/${params.id}`), { status: 404 })
    }
    const patch = (await request.json()) as components['schemas']['UpdateEventRequest']
    const zone = event.start.timeZone ?? mockCalendar.defaultTimeZone
    return response(200).json({
      ...event,
      title: patch.title ?? event.title,
      description: patch.description === '' ? null : (patch.description ?? event.description),
      location: patch.location === '' ? null : (patch.location ?? event.location),
      start: patch.start ? eventTime(patch.start, zone) : event.start,
      end: patch.end ? eventTime(patch.end, zone) : event.end,
      sequence: (event.sequence ?? 0) + (patch.start || patch.end ? 1 : 0),
    })
  }),
  http.delete('/api/v1/events/{id}', ({ response }) => response(204).empty()),
  http.patch(
    '/api/v1/events/{id}/occurrences/{recurrenceId}',
    async ({ params, request, response }) => {
      const occurrence =
        params.id === mockSeries.id
          ? seriesOccurrences().find((o) => o.recurrenceId === params.recurrenceId)
          : undefined
      if (!occurrence) {
        return response('default').json(
          notFound(`/api/v1/events/${params.id}/occurrences/${params.recurrenceId}`),
          { status: 404 },
        )
      }
      const patch = (await request.json()) as components['schemas']['UpdateOccurrenceRequest']
      const zone = occurrence.start.timeZone ?? mockCalendar.defaultTimeZone
      return response(200).json({
        ...occurrence,
        etag: undefined,
        title: patch.title ?? occurrence.title,
        location: patch.location === '' ? null : (patch.location ?? occurrence.location),
        start: patch.start ? eventTime(patch.start, zone) : occurrence.start,
        end: patch.end ? eventTime(patch.end, zone) : occurrence.end,
        modified: true,
      })
    },
  ),
  http.delete('/api/v1/events/{id}/occurrences/{recurrenceId}', ({ params, response }) =>
    params.id === mockSeries.id &&
    seriesOccurrences().some((o) => o.recurrenceId === params.recurrenceId)
      ? response(204).empty()
      : response('default').json(
          notFound(`/api/v1/events/${params.id}/occurrences/${params.recurrenceId}`),
          { status: 404 },
        ),
  ),
  http.post('/api/v1/events/{id}/split', async ({ params, request, response }) => {
    const body = await request.json()
    const occurrence =
      params.id === mockSeries.id
        ? seriesOccurrences().find((o) => o.recurrenceId === body.recurrenceId)
        : undefined
    if (!occurrence) {
      return response('default').json(notFound(`/api/v1/events/${params.id}/split`), {
        status: 404,
      })
    }
    const id = crypto.randomUUID()
    const zone = occurrence.start.timeZone ?? mockCalendar.defaultTimeZone
    return response(201).json({
      ...mockSeries,
      id,
      uid: `${id}@scalenderplus`,
      relatedTo: mockSeries.uid,
      title: body.title ?? mockSeries.title,
      start: body.start ? eventTime(body.start, zone) : occurrence.start,
      end: body.end ? eventTime(body.end, zone) : occurrence.end,
      recurrence: body.recurrence?.rrule ? { rrule: body.recurrence.rrule } : mockSeries.recurrence,
    })
  }),
  http.post('/api/v1/events/{id}/move', async ({ params, request, response }) => {
    const event = mockCalendars.events.find((e) => e.id === params.id)
    if (!event) {
      return response('default').json(notFound(`/api/v1/events/${params.id}`), { status: 404 })
    }
    const { targetCalendarId } = await request.json()
    return response(200).json({ ...event, calendarId: targetCalendarId ?? event.calendarId })
  }),
  http.get('/api/v1/events/{id}/overrides', ({ params, response }) =>
    params.id === mockEvent.id
      ? response(200).json({
          eventId: mockEvent.id,
          items: mockOverrides,
          etag: '"overrides-0401"',
        })
      : response('default').json(notFound(`/api/v1/events/${params.id}/overrides`), {
          status: 404,
        }),
  ),
  http.get('/api/v1/events/{id}/access/explain', ({ params, query, response }) => {
    if (params.id !== mockEvent.id) {
      return response('default').json(notFound(`/api/v1/events/${params.id}/access/explain`), {
        status: 404,
      })
    }
    const userId = query.get('userId') ?? mockUser.id
    // The mock user is the event's creator in a group calendar where members contribute: creator floor.
    return response(200).json({
      eventId: mockEvent.id,
      calendarId: mockEvent.calendarId,
      user: { id: userId, displayName: userId === mockUser.id ? mockUser.displayName : undefined },
      calendarLevel: 'contribute',
      level: 'manage',
      steps: [
        {
          kind: 'group_role_default',
          principal: { type: 'group', id: mockGroup.id },
          principalName: mockGroup.name,
          role: 'member',
          calendarLevel: 'contribute',
        },
        { kind: 'calendar_result', calendarLevel: 'contribute' },
        { kind: 'creator_floor', eventLevel: 'manage' },
        { kind: 'result', eventLevel: 'manage' },
      ],
    })
  }),
  http.put('/api/v1/events/{id}/overrides', async ({ params, request, response }) => {
    if (params.id !== mockEvent.id) {
      return response('default').json(notFound(`/api/v1/events/${params.id}/overrides`), {
        status: 404,
      })
    }
    const { overrides } = await request.json()
    return response(200).json({
      eventId: mockEvent.id,
      items: (overrides ?? []).map((o) => ({
        principal: { type: o.principal.type, id: o.principal.id, minRole: o.principal.minRole },
        level: o.level,
        createdBy: mockUser.id,
        createdAt: '2026-10-01T08:00:00Z',
      })),
      etag: `"overrides-${crypto.randomUUID().slice(0, 8)}"`,
    })
  }),
]

/** The occurrences of {@link mockSeries} (the series the occurrence endpoints know). */
function seriesOccurrences() {
  const series = mockCalendars.series.find((s) => s.master.id === mockSeries.id)
  return series ? mockOccurrences(series) : []
}

function notFound(instance: string): components['schemas']['ProblemDetails'] {
  return {
    type: 'https://scalenderplus.app/problems/not-found',
    title: 'Not found',
    status: 404,
    code: 'not_found',
    instance,
    traceId: '00-mock',
  }
}
