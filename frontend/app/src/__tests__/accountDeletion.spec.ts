import { afterEach, describe, expect, it } from 'vitest'
import {
  mockCredentials,
  mockSharedCalendarBlocker,
  mockSoleOwnerBlocker,
  resetMockAccount,
} from '@scalenderplus/api-client/mocks'
import { formatPurgeDate } from '@/lib/accountDeletion'
import { byTestId, click, mountApp, queryTestId, resetDom, settle, type, until } from './app'
import { useMockApi } from './msw'

const server = useMockApi()

afterEach(() => {
  server.events.removeAllListeners()
  resetDom()
})

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''

async function securityPage(locale: 'en' | 'de' = 'en') {
  const app = await mountApp('/settings/security', { locale })
  await until(
    () =>
      queryTestId('account-deletion-open') !== null ||
      queryTestId('account-deletion-scheduled') !== null,
  )
  return app
}

async function confirm(password: string) {
  await click('account-deletion-open')
  await until(() => queryTestId('account-deletion-dialog') !== null)
  await type('reauth-password', password)
  await click('reauth-submit')
  await settle()
}

describe('account deletion', () => {
  it('schedules the deletion with the password, shows the date everywhere and can be cancelled', async () => {
    const bodies: unknown[] = []
    server.events.on('request:start', async ({ request }) => {
      if (request.method === 'DELETE' && new URL(request.url).pathname === '/api/v1/me')
        bodies.push(await request.clone().json())
    })
    await securityPage()
    expect(text(byTestId('account-deletion'))).toContain('Group calendars stay with their groups')

    await confirm('wrong password!')
    expect(text(byTestId('account-deletion-dialog'))).toContain(
      'The password or code is incorrect.',
    )

    await type('reauth-password', mockCredentials.password)
    await click('reauth-submit')
    await until(() => queryTestId('account-deletion-scheduled') !== null)
    expect(bodies.at(-1)).toEqual({ password: mockCredentials.password })
    const purgeAt = new Date(Date.now() + 14 * 86_400_000).toISOString()
    expect(text(byTestId('account-deletion-scheduled'))).toContain(formatPurgeDate(purgeAt, 'en'))
    await until(() => queryTestId('account-deletion-banner') !== null)
    expect(text(byTestId('account-deletion-banner'))).toContain('Your account will be deleted on')

    await click('account-deletion-cancel')
    await until(() => queryTestId('account-deletion-open') !== null)
    await until(() => queryTestId('account-deletion-banner') === null)
    expect(text(document.body)).toContain('Deletion cancelled')
  })

  it('lists what blocks the deletion with links and keeps the button disabled', async () => {
    resetMockAccount({ blockers: [mockSoleOwnerBlocker, mockSharedCalendarBlocker] })
    await securityPage()

    const blockers = document.querySelectorAll('[data-testid="account-deletion-blocker"]')
    expect(blockers).toHaveLength(2)
    expect(text(blockers[0]!)).toContain('FC Lions')
    expect(text(blockers[0]!)).toContain('Only owner, 3 other members')
    expect(blockers[0]!.querySelector('a')!.getAttribute('href')).toBe(
      `/groups/${mockSoleOwnerBlocker.id}/members`,
    )
    expect(text(blockers[1]!)).toContain('shared with 2 people or groups')
    expect(blockers[1]!.querySelector('a')!.getAttribute('href')).toBe(
      `/calendars/${mockSharedCalendarBlocker.id}/settings`,
    )
    expect((byTestId('account-deletion-open') as HTMLButtonElement).disabled).toBe(true)
  })

  it('shows a scheduled deletion in German', async () => {
    resetMockAccount({ requestedAt: '2026-10-17T12:00:00Z' })
    await securityPage('de')

    expect(text(byTestId('account-deletion-scheduled'))).toContain(
      'Dein Konto wird am 31. Oktober 2026 gelöscht',
    )
    expect(text(byTestId('account-deletion-cancel'))).toBe('Löschung abbrechen')
  })
})
