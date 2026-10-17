import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  mockFullGroupEmail,
  mockGroup,
  mockGroupLimitName,
  mockPlanLimitName,
} from '@scalenderplus/api-client/mocks'
import { offerUpgrade } from '@/composables/paywall'
import { ApiError } from '@/lib/apiError'
import { isSuppressed, notNowDays, planKind, planProblemOf, suppress } from '@/lib/planLimits'
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

useMockApi()

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  resetDom()
})

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? ''
const day = 24 * 60 * 60 * 1000

const limitError = (extras: Record<string, unknown>) =>
  new ApiError(402, { code: 'plan_limit_reached', status: 402, ...extras })

describe('plan problems', () => {
  it('reads limit, plan and trial end of a 402', () => {
    expect(
      planProblemOf(
        limitError({
          limit: { key: 'events_with_overrides', max: 10, used: 10 },
          plan: 'free',
          trialEndsAt: null,
        }),
      ),
    ).toEqual({
      kind: 'limit',
      key: 'events_with_overrides',
      max: 10,
      used: 10,
      plan: 'free',
      trialEndsAt: null,
    })
    expect(
      planProblemOf(new ApiError(402, { code: 'feature_not_in_plan', feature: 'webhooks' })),
    ).toEqual({ kind: 'feature', feature: 'webhooks', plan: null, trialEndsAt: null })
    expect(planProblemOf(new ApiError(409, { code: 'calendar_frozen' }))).toBeNull()
    expect(planProblemOf(new Error('x'))).toBeNull()
  })

  it('tells the trial from paid Pro', () => {
    expect(planKind({ plan: 'pro', trialEndsAt: '2026-12-30T08:00:00Z' })).toBe('trial')
    expect(planKind({ plan: 'pro', trialEndsAt: null })).toBe('pro')
    expect(planKind({ plan: 'free', trialEndsAt: null })).toBe('free')
    expect(planKind({ plan: 'selfhost', trialEndsAt: null })).toBe('selfhost')
    expect(planKind({ plan: null, trialEndsAt: null })).toBe('unknown')
  })

  it('remembers "Not now" per key for 14 days', () => {
    const now = Date.parse('2026-10-17T12:00:00Z')
    suppress('limit:owned_groups', now)
    expect(isSuppressed('limit:owned_groups', now + 1)).toBe(true)
    expect(isSuppressed('limit:owned_calendars', now + 1)).toBe(false)
    expect(isSuppressed('limit:owned_groups', now + notNowDays * day - 1)).toBe(true)
    expect(isSuppressed('limit:owned_groups', now + notNowDays * day + 1)).toBe(false)
  })

  it('survives blocked or broken storage', () => {
    localStorage.setItem('scal.paywall.notNow', 'not json')
    expect(isSuppressed('limit:owned_groups')).toBe(false)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    expect(() => suppress('limit:owned_groups')).not.toThrow()
    expect(isSuppressed('limit:owned_groups')).toBe(false)
  })
})

describe('upgrade dialog', () => {
  async function createCalendarOverLimit() {
    await click('sidebar-new-calendar')
    await type('calendar-name', mockPlanLimitName)
    byTestId('create-calendar-submit').click()
    await until(() => queryTestId('form-error') !== null)
  }

  it('shows the numbers and the Free plan wording when creating a calendar fails', async () => {
    await mountApp('/calendar')
    await createCalendarOverLimit()

    const dialog = byTestId('paywall-dialog')
    expect(text(dialog)).toContain('Calendar limit reached')
    expect(text(byTestId('paywall-numbers'))).toBe('3 of 3')
    expect(text(byTestId('paywall-plan'))).toBe('This is the limit of the Free plan.')
    expect(text(byTestId('paywall-benefit'))).toContain('Pro has room for many more calendars')
    expect(text(byTestId('paywall-coming-soon'))).toContain('coming soon')
    // The form keeps its own explanation.
    expect(text(byTestId('form-error'))).not.toBe('')
  })

  it('"Not now" hides the same dialog for 14 days, "OK" does not', async () => {
    await mountApp('/calendar')
    await createCalendarOverLimit()
    await click('paywall-ok')
    expect(queryTestId('paywall-dialog')).toBeNull()

    byTestId('create-calendar-submit').click()
    await until(() => queryTestId('paywall-dialog') !== null)
    await click('paywall-not-now')
    expect(queryTestId('paywall-dialog')).toBeNull()

    byTestId('create-calendar-submit').click()
    await settle()
    expect(queryTestId('paywall-dialog')).toBeNull()
    expect(text(byTestId('form-error'))).not.toBe('')

    // Another limit still shows its dialog.
    offerUpgrade(limitError({ limit: { key: 'owned_groups', max: 1, used: 1 }, plan: 'free' }))
    await settle()
    expect(text(byTestId('paywall-dialog'))).toContain('Group limit reached')
  })

  it('uses the trial wording with its end date', async () => {
    await mountApp('/groups')
    await until(() => queryTestId('create-group') !== null)
    await click('create-group')
    await type('group-name', mockGroupLimitName)
    await submit('create-group-form')
    await until(() => queryTestId('paywall-dialog') !== null)

    expect(text(byTestId('paywall-dialog'))).toContain('Group limit reached')
    expect(text(byTestId('paywall-numbers'))).toBe('10 of 10')
    expect(text(byTestId('paywall-plan'))).toBe(
      'This is the limit of Pro, which applies during the trial until December 30, 2026.',
    )
    expect(text(byTestId('paywall-benefit'))).toContain('After the trial ends on December 30, 2026')
  })

  it('offers it when an invite would exceed the group size', async () => {
    await mountApp(`/groups/${mockGroup.id}/invites`)
    await until(() => queryTestId('invite-email') !== null)
    await type('invite-email', mockFullGroupEmail)
    await submit('invite-email-form')
    await until(() => queryTestId('paywall-dialog') !== null)

    expect(text(byTestId('paywall-dialog'))).toContain('This group is full')
    expect(text(byTestId('paywall-numbers'))).toBe('15 of 15')
    // The mock user is the group's billing owner: they can upgrade.
    expect(queryTestId('paywall-owner-only')).toBeNull()
  })

  it('says only the owner can upgrade when the limit is someone else’s', async () => {
    await mountApp('/calendar')
    offerUpgrade(
      limitError({ limit: { key: 'members_per_group', max: 15, used: 15 }, plan: 'free' }),
      { owner: 'other' },
    )
    await settle()
    expect(queryTestId('paywall-owner-only')).not.toBeNull()
    expect(queryTestId('paywall-coming-soon')).toBeNull()
    expect(queryTestId('paywall-benefit')).toBeNull()
  })

  it('words paid Pro, self-hosted limits and missing features', async () => {
    await mountApp('/calendar')
    offerUpgrade(
      limitError({ limit: { key: 'overrides_per_event', max: 25, used: 25 }, plan: 'pro' }),
    )
    await settle()
    expect(text(byTestId('paywall-plan'))).toBe('This is the limit of the Pro plan.')
    expect(queryTestId('paywall-benefit')).toBeNull()

    offerUpgrade(
      limitError({ limit: { key: 'owned_calendars', max: 5, used: 5 }, plan: 'selfhost' }),
    )
    await settle()
    expect(text(byTestId('paywall-plan'))).toBe('The operator of this server has set this limit.')
    expect(queryTestId('paywall-coming-soon')).toBeNull()

    offerUpgrade(
      new ApiError(402, { code: 'feature_not_in_plan', feature: 'webhooks', plan: 'free' }),
    )
    await settle()
    expect(text(byTestId('paywall-dialog'))).toContain('Not included in this plan')
    expect(text(byTestId('paywall-plan'))).toBe("This feature isn't part of the Free plan.")
    expect(queryTestId('paywall-usage')).toBeNull()
  })

  it('speaks German', async () => {
    await mountApp('/calendar', { locale: 'de' })
    await createCalendarOverLimit()
    expect(text(byTestId('paywall-dialog'))).toContain('Kalender-Limit erreicht')
    expect(text(byTestId('paywall-numbers'))).toBe('3 von 3')
  })
})
