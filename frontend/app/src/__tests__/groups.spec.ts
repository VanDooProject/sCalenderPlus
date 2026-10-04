import { afterEach, describe, expect, it } from 'vitest'
import {
  mockGroup,
  mockGroups,
  mockMemberGroup,
  mockMembers,
} from '@scalenderplus/api-client/mocks'
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

const [mia, adam, max, vic] = mockMembers.map((m) => m.userId) as [string, string, string, string]
const lions = () => mockGroups.groups.find((g) => g.id === mockGroup.id)!
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''

interface Recorded {
  method: string
  path: string
  search: string
  ifMatch: string | null
  body: unknown
}

/** Records the api's mutating requests (method, path, query, If-Match, body). */
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

async function openMenu(testid: string) {
  byTestId(testid).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
  await settle()
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

async function membersPage(groupId = mockGroup.id) {
  const app = await mountApp(`/groups/${groupId}/members`)
  await until(() => queryTestId('member-list') !== null)
  return app
}

describe('groups list', () => {
  it('lists my groups with my role and the member count', async () => {
    await mountApp('/groups')
    await until(() => queryTestId('group-list') !== null)
    const card = byTestId(`group-card-${mockGroup.id}`)
    expect(text(card.querySelector('[data-testid=group-card-name]'))).toBe('FC Lions')
    expect(text(card.querySelector('[data-testid=role-badge]'))).toBe('Owner')
    expect(text(card.querySelector('[data-testid=group-card-members]'))).toBe('4 members')
    const books = byTestId(`group-card-${mockMemberGroup.id}`)
    expect(text(books.querySelector('[data-testid=role-badge]'))).toBe('Member')
    // The sidebar lists them too.
    expect(text(byTestId('sidebar-groups'))).toContain('FC Lions')
    expect(text(byTestId('sidebar-groups'))).toContain('Book club')
  })

  it('creates a group and opens it', async () => {
    const { router } = await mountApp('/groups')
    await until(() => queryTestId('create-group') !== null)
    await click('create-group')
    await submit('create-group-form')
    expect(text(byTestId('field-error'))).toBe('Please fill in this field.')

    await type('group-name', ' Tennis ')
    await type('group-description', 'Saturdays')
    await submit('create-group-form')
    await until(() => router.currentRoute.value.name === 'group-members')
    await until(() => queryTestId('member-list') !== null)

    expect(text(byTestId('group-title'))).toBe('Tennis')
    expect(text(byTestId('member-list'))).toContain('Mia')
    expect(text(byTestId('toast'))).toContain('Group Tennis created.')
    expect(text(byTestId('sidebar-groups'))).toContain('Tennis')
  })
})

describe('members', () => {
  it('shows role badges, the billing owner and only the actions my role allows', async () => {
    await membersPage()
    const row = (id: string) => byTestId(`member-${id}`)
    expect(text(row(mia))).toContain('You')
    expect(row(mia).querySelector('[data-testid=billing-owner-badge]')).not.toBeNull()
    expect(text(row(adam).querySelector('[data-testid=role-badge]'))).toBe('Admin')
    // Mia is the only owner: a hint explains why she can't leave yet.
    expect(text(row(mia))).toContain('Only owner: make someone else an owner first.')

    await openMenu(`member-actions-${adam}`)
    const items = [...document.querySelectorAll('[role=menuitem]')].map((i) => text(i))
    expect(items).toEqual(['Change role', 'Remove from group'])
  })

  it('changes a role with the member ETag and keeps event shares when unchecked', async () => {
    const requests = recordMutations()
    await membersPage()
    await openMenu(`member-actions-${max}`)
    await click('action-change-role')
    expect(text(byTestId('role-dialog'))).toContain('Change role of Max')

    await choose('role-select', 'Viewer')
    // A demotion offers to keep or revoke personal event shares (default: revoke).
    const checkbox = byTestId('role-dialog').querySelector<HTMLButtonElement>('[role=checkbox]')!
    expect(checkbox.getAttribute('aria-checked')).toBe('true')
    checkbox.click()
    await settle()
    await click('role-submit')

    expect(requests).toEqual([
      {
        method: 'PATCH',
        path: `/api/v1/groups/${mockGroup.id}/members/${max}`,
        search: '?revokeEventShares=false',
        ifMatch: '"0003-1"',
        body: { role: 'viewer' },
      },
    ])
    expect(text(byTestId('toast'))).toContain('Max is now Viewer.')
    await until(() => text(byTestId(`member-${max}`)).includes('Viewer'))
  })

  it('removes a member and revokes their event shares by default', async () => {
    const requests = recordMutations()
    await membersPage()
    await openMenu(`member-actions-${vic}`)
    await click('action-remove')
    expect(text(byTestId('remove-dialog'))).toContain('Remove Vic?')
    expect(
      byTestId('remove-dialog').querySelector('[role=checkbox]')?.getAttribute('aria-checked'),
    ).toBe('true')
    await click('remove-submit')

    expect(requests).toEqual([
      {
        method: 'DELETE',
        path: `/api/v1/groups/${mockGroup.id}/members/${vic}`,
        search: '',
        ifMatch: '"0004-1"',
        body: null,
      },
    ])
    await until(() => queryTestId(`member-${vic}`) === null)
    expect(text(byTestId('toast'))).toContain('Vic was removed from the group.')
    expect(text(byTestId('group-member-count'))).toBe('3 members')
  })

  it('explains the last-owner rule when the only owner tries to leave', async () => {
    await membersPage()
    await click('leave-group')
    await click('remove-submit')
    expect(text(byTestId('form-error'))).toBe(
      'You are the only owner. Make another member an owner before you leave, or delete the group.',
    )
    expect(lions().members).toHaveLength(4)
  })

  it('explains that the billing owner has to transfer billing first, then transfers it', async () => {
    lions().members.find((m) => m.userId === adam)!.role = 'owner'
    await membersPage()
    await click('leave-group')
    await click('remove-submit')
    expect(text(byTestId('form-error'))).toContain('You are the billing owner.')
    ;(
      document.querySelector('[data-testid=leave-dialog] button[aria-label=Close]') as HTMLElement
    ).click()
    await settle()

    await openMenu(`member-actions-${adam}`)
    await click('action-transfer')
    await click('transfer-submit')
    expect(lions().billingOwnerId).toBe(adam)
    await until(
      () => byTestId(`member-${adam}`).querySelector('[data-testid=billing-owner-badge]') !== null,
    )
  })

  it('leaves a group as a member and forgets it', async () => {
    const requests = recordMutations()
    const { router } = await membersPage(mockMemberGroup.id)
    // A member manages nobody: no actions on the owner's row.
    const owner = mockGroups.groups.find((g) => g.id === mockMemberGroup.id)!.members[0]!
    expect(queryTestId(`member-actions-${owner.userId}`)).toBeNull()

    await click('leave-group')
    expect(text(byTestId('leave-dialog'))).toContain('Leave Book club?')
    await click('remove-submit')
    await until(() => router.currentRoute.value.name === 'groups')

    expect(requests.map((r) => `${r.method} ${r.path}${r.search}`)).toEqual([
      `DELETE /api/v1/groups/${mockMemberGroup.id}/members/${mia}`,
    ])
    expect(text(byTestId('toast'))).toContain('You left Book club.')
    await until(() => queryTestId(`group-card-${mockMemberGroup.id}`) === null)
  })

  it('reloads after a conflicting change and tells the user', async () => {
    await membersPage()
    // Someone else changed Max in the meantime: the cached ETag is stale.
    lions().members.find((m) => m.userId === max)!.version = 5
    await openMenu(`member-actions-${max}`)
    await click('action-change-role')
    await choose('role-select', 'Admin')
    await click('role-submit')
    expect(text(byTestId('form-error'))).toMatch(/Someone changed this in the meantime/)

    await settle()
    await click('role-submit')
    expect(lions().members.find((m) => m.userId === max)!.role).toBe('admin')
  })
})

describe('invites', () => {
  it('creates an invite link, shows it once and lists it', async () => {
    const requests = recordMutations()
    await mountApp(`/groups/${mockGroup.id}/invites`)
    await until(() => queryTestId('invite-list') !== null)
    expect(byTestId('invite-list').querySelectorAll('li')).toHaveLength(1)

    await type('invite-link-max-uses', '5')
    await submit('invite-link-form')

    expect(requests.at(-1)?.body).toEqual({ role: 'member', expiresInDays: 7, maxUses: 5 })
    const url = (byTestId('invite-link-url') as HTMLInputElement).value
    expect(url).toMatch(/\/invite\?token=mock-invite-\d+$/)
    await until(() => byTestId('invite-list').querySelectorAll('li').length === 2)
  })

  it('offers links only up to member and email invites up to owner', async () => {
    await mountApp(`/groups/${mockGroup.id}/invites`)
    await until(() => queryTestId('invite-list') !== null)
    byTestId('invite-link-role').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    )
    await settle()
    expect([...document.querySelectorAll('[role=option]')].map((o) => text(o))).toEqual([
      'Viewer',
      'Member',
    ])
  })

  it('sends an email invite and revokes it', async () => {
    await mountApp(`/groups/${mockGroup.id}/invites`)
    await until(() => queryTestId('invite-list') !== null)
    await type('invite-email', 'nora@example.test')
    await choose('invite-email-role', 'Admin')
    await submit('invite-email-form')
    expect(text(byTestId('toast'))).toContain('Invitation sent to nora@example.test.')
    await until(() => text(byTestId('invite-list')).includes('nora@example.test'))

    const row = [...byTestId('invite-list').querySelectorAll('li')].find((li) =>
      text(li).includes('nora@example.test'),
    )!
    ;(row.querySelector('[data-testid=invite-revoke]') as HTMLElement).click()
    await settle()
    await click('revoke-submit')
    await until(() => !text(byTestId('invite-list')).includes('nora@example.test'))
  })

  it('hides the invites tab from members', async () => {
    await membersPage(mockMemberGroup.id)
    expect(queryTestId('tab-invites')).toBeNull()
    expect(queryTestId('tab-settings')).not.toBeNull()
  })
})

describe('group settings', () => {
  it('saves details with the group ETag', async () => {
    const requests = recordMutations()
    await mountApp(`/groups/${mockGroup.id}/settings`)
    await until(() => queryTestId('group-settings-form') !== null)
    await type('settings-group-name', 'FC Lions 1910')
    await submit('group-settings-form')

    expect(requests).toEqual([
      {
        method: 'PATCH',
        path: `/api/v1/groups/${mockGroup.id}`,
        search: '',
        ifMatch: `"group-${mockGroup.id.slice(-4)}-1"`,
        body: { name: 'FC Lions 1910' },
      },
    ])
    await until(() => text(byTestId('group-title')) === 'FC Lions 1910')
  })

  it('explains why a group with calendars cannot be deleted', async () => {
    await mountApp(`/groups/${mockGroup.id}/settings`)
    await until(() => queryTestId('settings-delete') !== null)
    await click('settings-delete')
    expect((byTestId('delete-group-submit') as HTMLButtonElement).disabled).toBe(true)
    await type('delete-group-confirm', 'FC Lions')
    await click('delete-group-submit')
    expect(text(byTestId('form-error'))).toContain('This group still owns calendars.')
  })

  it('is read-only for members', async () => {
    await mountApp(`/groups/${mockMemberGroup.id}/settings`)
    await until(() => queryTestId('group-settings-form') !== null)
    expect(queryTestId('group-settings-readonly')).not.toBeNull()
    expect((byTestId('settings-group-name') as HTMLInputElement).disabled).toBe(true)
    expect(queryTestId('settings-delete')).toBeNull()
  })

  it('shows a not-found page for groups I am not in', async () => {
    await mountApp('/groups/0192f2c4-0000-7000-8000-000000000999/members')
    await until(() => document.querySelector('h1')?.textContent === 'Group not found')
  })
})
