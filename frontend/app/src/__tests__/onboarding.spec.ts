import { afterEach, describe, expect, it } from 'vitest'
import type { EventResponse } from '@scalenderplus/api-client'
import { mockEvent, mockNewUser, mockUser, resetMockAuth } from '@scalenderplus/api-client/mocks'
import { resetOnboardingState, triesCoreFeature } from '@/composables/onboarding'
import {
  byTestId,
  click,
  mountApp,
  queryTestId,
  resetDom,
  settle,
  submit,
  type,
  unmountApp,
  until,
} from './app'
import { useMockApi } from './msw'

useMockApi()

afterEach(() => {
  resetDom()
  resetOnboardingState()
})

const step = (name: string) => byTestId(`onboarding-step-${name}`)
const isDone = (name: string) => step(name).dataset.done === 'true'
const progress = () => byTestId('onboarding-progress').textContent?.trim()

async function openUserMenu() {
  byTestId('user-menu-trigger').dispatchEvent(
    new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
  )
  await settle()
}

describe('onboarding checklist', () => {
  it('stays away for an account that is set up already, and reopens from the user menu', async () => {
    await mountApp('/calendar')
    expect(queryTestId('onboarding')).toBeNull()
    expect(localStorage.getItem(`scal.onboarding.${mockUser.id}`)).toBe('hidden')

    await openUserMenu()
    await click('onboarding-reopen')
    await until(() => queryTestId('onboarding') !== null)
    expect(progress()).toBe('3 of 3 done')
    expect(isDone('calendar') && isDone('group') && isDone('feed')).toBe(true)
    expect(document.activeElement?.id).toBe('onboarding-title')
    expect(localStorage.getItem(`scal.onboarding.${mockUser.id}`)).toBeNull()

    await click('onboarding-close')
    expect(queryTestId('onboarding')).toBeNull()
    expect(localStorage.getItem(`scal.onboarding.${mockUser.id}`)).toBe('hidden')
  })

  it('walks a new account through calendar, invite link and feed link', async () => {
    mockNewUser()
    const { router } = await mountApp('/calendar')
    await until(() => queryTestId('onboarding') !== null)

    const list = byTestId('onboarding-steps')
    expect(list.tagName).toBe('OL')
    expect(progress()).toBe('0 of 3 done')
    expect(step('calendar').getAttribute('aria-current')).toBe('step')
    expect(step('calendar').textContent).toContain('(To do)')
    expect(step('event').textContent).toContain('Optional')
    // No calendar yet: nothing to add an event to.
    expect(queryTestId('onboarding-event')).toBeNull()

    // 1. A calendar, named after the user; the checklist stays where it is.
    await click('onboarding-calendar')
    expect((byTestId('calendar-name') as HTMLInputElement).value).toBe(mockUser.displayName)
    byTestId('create-calendar-submit').click()
    await until(() => isDone('calendar'))
    expect(router.currentRoute.value.name).toBe('calendar')
    expect(step('calendar').textContent).toContain('(Done)')
    expect(step('group').getAttribute('aria-current')).toBe('step')
    expect(progress()).toBe('1 of 3 done')

    // 2. A group, then its invite link (with a QR code).
    await click('onboarding-group')
    await type('group-name', 'Climbing')
    await submit('create-group-form')
    await until(() => router.currentRoute.value.name === 'group-invites')
    expect(isDone('group')).toBe(false)
    expect(queryTestId('onboarding-invite')).not.toBeNull()
    await until(() => queryTestId('invite-link-form') !== null)
    await submit('invite-link-form')
    await until(() => queryTestId('invite-link-created') !== null)
    expect(byTestId('invite-link-created').querySelector('[data-testid="qr-code"]')).not.toBeNull()
    await until(() => isDone('group'))

    // 3. A feed link of everything.
    await click('onboarding-feed')
    await submit('create-feed-form')
    await until(() => queryTestId('created-feed') !== null)
    await until(() => isDone('feed'))
    expect(progress()).toBe('3 of 3 done')
    expect(byTestId('onboarding').textContent).toContain("You're all set")
    await click('created-feed-done')

    await click('onboarding-close')
    expect(queryTestId('onboarding')).toBeNull()

    // Closed for good: a reload does not bring it back.
    unmountApp()
    resetOnboardingState()
    await mountApp('/calendar')
    expect(queryTestId('onboarding')).toBeNull()
  })

  it('opens the new event dialog from the optional step', async () => {
    mockNewUser()
    const { router } = await mountApp('/calendar')
    await until(() => queryTestId('onboarding') !== null)
    await click('onboarding-calendar')
    byTestId('create-calendar-submit').click()
    await until(() => queryTestId('onboarding-event') !== null)

    byTestId('onboarding-event').click()
    await until(() => queryTestId('event-dialog') !== null)
    expect(router.currentRoute.value.query.new).toBeUndefined()
    await type('event-title', 'Dentist')
    await type('event-cover-title', 'Appointment')
    byTestId('event-save').click()
    await until(() => isDone('event'))
  })

  it('can be hidden, says where to find it again and stays hidden', async () => {
    mockNewUser()
    await mountApp('/groups')
    await until(() => queryTestId('onboarding') !== null)
    await click('onboarding-hide')
    expect(queryTestId('onboarding')).toBeNull()
    expect(byTestId('toast').textContent).toContain('reopen it from your account menu')

    unmountApp()
    resetOnboardingState()
    await mountApp('/groups')
    expect(queryTestId('onboarding')).toBeNull()
    await openUserMenu()
    await click('onboarding-reopen')
    await until(() => queryTestId('onboarding') !== null)
    expect(progress()).toBe('0 of 3 done')
  })

  it('asks unverified users to confirm their email before inviting', async () => {
    resetMockAuth({ user: { emailVerified: false } })
    mockNewUser()
    await mountApp('/calendar')
    await until(() => queryTestId('onboarding') !== null)
    expect(byTestId('onboarding-verify-first').textContent).toContain('Confirm your email address')
  })

  it('speaks German', async () => {
    mockNewUser()
    await mountApp('/calendar', { locale: 'de' })
    await until(() => queryTestId('onboarding') !== null)
    expect(byTestId('onboarding').textContent).toContain('Lade deine Gruppe ein')
    expect(progress()).toBe('0 von 3 erledigt')
  })
})

describe('triesCoreFeature', () => {
  const me = mockUser.id
  const event = (fields: Partial<EventResponse>): EventResponse => ({ ...mockEvent, ...fields })

  it('counts own events with a cover title or their own permissions', () => {
    expect(triesCoreFeature(event({ coverTitle: 'Busy-ish', myLevel: 'manage' }), me)).toBe(true)
    expect(triesCoreFeature(event({ hasOverrides: true, myLevel: 'edit' }), me)).toBe(true)
    expect(triesCoreFeature(event({ coverTitle: ' ', hasOverrides: false }), me)).toBe(false)
  })

  it('ignores what others set on events the user only sees', () => {
    const other = { id: '0192f2c4-0000-7000-8000-000000000009', displayName: 'Olga' }
    expect(
      triesCoreFeature(event({ coverTitle: 'Cover', myLevel: 'free_busy', createdBy: other }), me),
    ).toBe(false)
    expect(
      triesCoreFeature(event({ hasOverrides: true, myLevel: 'read', createdBy: other }), me),
    ).toBe(false)
  })
})
