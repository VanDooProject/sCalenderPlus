import { afterEach, describe, expect, it } from 'vitest'
import {
  mockGroups,
  mockInvitedGroup,
  mockInviteTokens,
  resetMockAuth,
} from '@scalenderplus/api-client/mocks'
import { byTestId, click, mountApp, queryTestId, resetDom, until } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(() => {
  server.events.removeAllListeners()
  resetDom()
})

const invitePath = (token: string) => `/invite?token=${token}`
const text = (id: string) => byTestId(id).textContent?.replace(/\s+/g, ' ').trim() ?? ''

describe('invite page', () => {
  it('shows the group, inviter, role and expiry and joins when signed in', async () => {
    const { router } = await mountApp(invitePath(mockInviteTokens.valid))

    expect(document.querySelector('h1')?.textContent).toBe('Join Choir')
    expect(text('invite-preview')).toContain('Olga invited you to join as Member.')
    expect(text('invite-expiry')).toMatch(/expires October 8, 2026/)

    await click('invite-accept')
    await until(() => router.currentRoute.value.name !== 'invite')

    expect(router.currentRoute.value.name).toBe('group-members')
    expect(router.currentRoute.value.params.groupId).toBe(mockInvitedGroup.id)
    expect(byTestId('toast').textContent).toContain('You joined Choir.')
    const choir = mockGroups.groups.find((g) => g.id === mockInvitedGroup.id)!
    expect(choir.members.map((m) => m.displayName)).toContain('Mia')
  })

  it('sends signed-out invitees through login or sign-up and back', async () => {
    resetMockAuth({ signedIn: false })
    await mountApp(invitePath(mockInviteTokens.valid))

    expect(document.querySelector('h1')?.textContent).toBe('Join Choir')
    const next = encodeURIComponent(invitePath(mockInviteTokens.valid))
      .replaceAll('%2F', '/')
      .replaceAll('%3F', '?')
      .replaceAll('%3D', '=')
    expect(byTestId('invite-login').getAttribute('href')).toBe(`/login?next=${next}`)
    expect(byTestId('invite-register').getAttribute('href')).toBe(`/register?next=${next}`)
    expect(queryTestId('invite-accept')).toBeNull()
  })

  it('says when the invitation is invalid, expired or used', async () => {
    await mountApp(invitePath(mockInviteTokens.invalid))
    expect(text('invite-expired')).toContain('This invitation can’t be used'.replace('’', "'"))
    expect(byTestId('invite-expired').querySelector('a')?.getAttribute('href')).toBe('/groups')
  })

  it('explains an email invitation for another address and offers to switch accounts', async () => {
    const { router } = await mountApp(invitePath(mockInviteTokens.otherEmail))
    await click('invite-accept')

    expect(text('invite-mismatch')).toContain('You are signed in as mia@example.test')
    ;(byTestId('invite-mismatch').querySelector('button') as HTMLButtonElement).click()
    await until(() => router.currentRoute.value.name === 'login')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe(invitePath(mockInviteTokens.otherEmail))
  })

  it('asks unverified users to confirm their email first', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    await mountApp(invitePath(mockInviteTokens.valid))
    expect(text('invite-unverified')).toContain('Confirm your email address first')
  })

  it('explains that a frozen group takes no new members', async () => {
    await mountApp(invitePath(mockInviteTokens.frozen))
    await click('invite-accept')
    expect(text('invite-frozen')).toContain("This group can't take new members right now")
  })

  it('treats a link without token as incomplete', async () => {
    await mountApp('/invite')
    expect(queryTestId('invite-invalid')).not.toBeNull()
  })

  it('keeps tokens out of URLs sent to the api (preview and accept post the token)', async () => {
    const urls: string[] = []
    server.events.on('request:start', ({ request }) => {
      urls.push(`${request.method} ${new URL(request.url).pathname}${new URL(request.url).search}`)
    })
    await mountApp(invitePath(mockInviteTokens.valid))
    await click('invite-accept')
    expect(urls.filter((u) => u.includes('mock-invite'))).toEqual([])
    expect(urls).toContain('POST /api/v1/invites/preview')
    expect(urls).toContain('POST /api/v1/invites/accept')
  })
})
