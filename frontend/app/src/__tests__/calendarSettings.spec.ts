import { afterEach, describe, expect, it } from 'vitest'
import {
  mockCalendar,
  mockCalendars,
  mockFrozenCalendar,
  mockGrant,
  mockPlanLimitName,
  mockReadOnlyCalendar,
} from '@scalenderplus/api-client/mocks'
import { ApiError } from '@/lib/apiError'
import {
  blockedByFrozen,
  calendarErrorMessage,
  canDeleteCalendar,
  canManageCalendar,
  grantableLevels,
  roleDefaultLevels,
} from '@/lib/calendarLevels'
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
  search: string
  ifMatch: string | null
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
      search: url.search,
      ifMatch: request.headers.get('If-Match'),
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

async function settingsPage(id = mockCalendar.id) {
  const app = await mountApp(`/calendars/${id}/settings`)
  await until(() => queryTestId('calendar-settings-title') !== null)
  return app
}

const problem = (code: string, status: number) => new ApiError(status, { code })
const t = (key: string) => key

describe('calendar levels', () => {
  it('gates settings and deletion by myLevel', () => {
    expect(canManageCalendar('manage')).toBe(true)
    expect(canManageCalendar('owner')).toBe(true)
    expect(canManageCalendar('edit')).toBe(false)
    expect(canDeleteCalendar('manage')).toBe(false)
    expect(canDeleteCalendar('owner')).toBe(true)
    expect(canManageCalendar('something-new')).toBe(false)
  })

  it('offers grant levels free_busy … manage, never above the own level', () => {
    expect(grantableLevels('owner')).toEqual(['free_busy', 'read', 'contribute', 'edit', 'manage'])
    expect(grantableLevels('manage')).toEqual(['free_busy', 'read', 'contribute', 'edit', 'manage'])
    expect(roleDefaultLevels('owner')[0]).toBe('none')
  })

  it('blocks only new and raised grants on frozen calendars', () => {
    expect(blockedByFrozen(true, null, 'read')).toBe(true)
    expect(blockedByFrozen(true, 'read', 'edit')).toBe(true)
    expect(blockedByFrozen(true, 'edit', 'read')).toBe(false)
    expect(blockedByFrozen(true, 'edit', null)).toBe(false)
    expect(blockedByFrozen(false, null, 'manage')).toBe(false)
  })

  it('maps the calendar error codes to explanations', () => {
    expect(calendarErrorMessage(t, problem('permission_self_lockout', 409))).toBe(
      'calendarSettings.errors.selfLockout',
    )
    expect(calendarErrorMessage(t, problem('calendar_frozen', 409))).toBe(
      'calendarSettings.errors.frozen',
    )
    expect(calendarErrorMessage(t, problem('plan_limit_reached', 402))).toBe(
      'calendarSettings.errors.planLimit',
    )
    expect(calendarErrorMessage(t, problem('precondition_failed', 412))).toBe(
      'calendarSettings.errors.changed',
    )
    expect(calendarErrorMessage(t, problem('not_found', 404))).toBe('errors.code.not_found')
  })
})

describe('calendar settings page', () => {
  it('shows every section to the owner and saves the name with If-Match', async () => {
    const requests = recordMutations()
    await settingsPage()
    expect(queryTestId('calendar-permissions-form')).not.toBeNull()
    expect(queryTestId('grants')).not.toBeNull()
    expect(queryTestId('calendar-delete')).not.toBeNull()
    expect(text(byTestId(`grant-${mockGrant.principal.id}`))).toContain('Eve')

    await type('calendar-settings-name', 'FC Lions – Training')
    await submit('calendar-general-form')
    await until(() => mockCalendars.calendars[0]!.name === 'FC Lions – Training')
    expect(requests).toEqual([
      expect.objectContaining({
        method: 'PATCH',
        path: `/api/v1/calendars/${mockCalendar.id}`,
        ifMatch: '"cal-0301-1"',
        body: { name: 'FC Lions – Training' },
      }),
    ])
  })

  it('is read-only below manage: no permissions, sharing or danger zone', async () => {
    await settingsPage(mockReadOnlyCalendar.id)
    expect(queryTestId('calendar-settings-readonly')).not.toBeNull()
    expect((byTestId('calendar-settings-name') as HTMLInputElement).disabled).toBe(true)
    expect(queryTestId('calendar-permissions-form')).toBeNull()
    expect(queryTestId('grants')).toBeNull()
    expect(queryTestId('calendar-delete')).toBeNull()
  })

  it('asks about event shares when a role default is lowered', async () => {
    const requests = recordMutations()
    await settingsPage()
    expect(queryTestId('role-defaults-revoke')).toBeNull()
    await choose('role-default-member', 'View')
    expect(queryTestId('role-defaults-revoke')).not.toBeNull()
    byTestId('role-defaults-revoke').querySelector<HTMLElement>('[role=checkbox]')!.click()
    await settle()
    await submit('calendar-permissions-form')
    await until(() => requests.length === 1)
    expect(requests[0]).toMatchObject({
      method: 'PATCH',
      search: '?revokeEventShares=false',
      body: { groupRoleDefaults: { member: 'read' } },
    })
  })

  it('keeps cleanup enabled on a frozen calendar', async () => {
    await settingsPage(mockFrozenCalendar.id)
    expect(queryTestId('calendar-frozen')).not.toBeNull()
    expect((byTestId('calendar-settings-name') as HTMLInputElement).disabled).toBe(true)
    expect((byTestId('grant-add') as HTMLButtonElement).disabled).toBe(true)
    expect((byTestId('calendar-delete') as HTMLButtonElement).disabled).toBe(false)
    const remove = byTestId('grant-remove-0192f2c4-0000-7000-8000-000000000002')
    expect((remove as HTMLButtonElement).disabled).toBe(false)
  })

  it('removes a grant and keeps event shares when unchecked', async () => {
    const requests = recordMutations()
    await settingsPage()
    await click(`grant-remove-${mockGrant.principal.id}`)
    byTestId('grant-revoke').querySelector<HTMLElement>('[role=checkbox]')!.click()
    await settle()
    byTestId('grant-confirm-submit').click()
    await until(() => requests.length === 1)
    expect(requests[0]).toMatchObject({
      method: 'DELETE',
      path: `/api/v1/calendars/${mockCalendar.id}/grants/${mockGrant.id}`,
      search: '?revokeEventShares=false',
      ifMatch: mockGrant.etag,
    })
    await until(() => queryTestId('grants-empty') !== null)
  })

  it('reloads on 412 and keeps the unsaved edit', async () => {
    await settingsPage()
    await type('calendar-settings-name', 'My edit')
    // Someone else saved in the meantime.
    mockCalendars.versions[mockCalendar.id] = 7
    await submit('calendar-general-form')
    await until(() => text(byTestId('form-error')).includes('Someone else changed'))
    expect((byTestId('calendar-settings-name') as HTMLInputElement).value).toBe('My edit')
    // The next try uses the reloaded version.
    await submit('calendar-general-form')
    await until(() => mockCalendars.calendars[0]!.name === 'My edit')
  })

  it('deletes only after typing the name', async () => {
    const { router } = await settingsPage(mockFrozenCalendar.id)
    await click('calendar-delete')
    const confirm = byTestId('delete-calendar-submit') as HTMLButtonElement
    expect(confirm.disabled).toBe(true)
    await type('delete-calendar-confirm', 'Old team')
    expect(confirm.disabled).toBe(false)
    confirm.click()
    await until(() => router.currentRoute.value.name === 'calendar')
    expect(mockCalendars.calendars.some((c) => c.id === mockFrozenCalendar.id)).toBe(false)
  })
})

describe('create calendar dialog', () => {
  it('creates a personal calendar and opens its settings', async () => {
    const { router } = await mountApp('/calendar')
    await click('sidebar-new-calendar')
    await type('calendar-name', 'Family')
    byTestId('create-calendar-submit').click()
    await until(() => router.currentRoute.value.name === 'calendar-settings')
    const created = mockCalendars.calendars.find((c) => c.name === 'Family')
    expect(created?.owner.type).toBe('user')
  })

  it('explains the plan limit', async () => {
    await mountApp('/calendar')
    await click('sidebar-new-calendar')
    await type('calendar-name', mockPlanLimitName)
    byTestId('create-calendar-submit').click()
    await until(() => queryTestId('form-error') !== null)
    expect(text(byTestId('form-error'))).toContain('See plans')
  })
})
