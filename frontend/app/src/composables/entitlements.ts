import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import type { components } from '@scalenderplus/api-client'
import { api } from '@/api'
import { call } from '@/lib/apiError'
import { useSession } from './session'

/** My plan, the end of the reverse trial and the limits (docs/product/plans.md). */
export type Entitlements = components['schemas']['EntitlementsResponse']

/** Prefix of the entitlements queries; one per user, so a new session never sees the last one's. */
export const entitlementsQueryKey = ['entitlements'] as const

/**
 * `GET /me/entitlements` of the signed-in user (signed-in pages only; plan and limits change rarely,
 * refreshed when the window regains focus after the stale time).
 */
export function useEntitlements() {
  const { user } = useSession()
  const userId = computed(() => user.value?.id ?? null)
  const query = useQuery({
    queryKey: [...entitlementsQueryKey, userId],
    queryFn: async () => (await call(api.GET('/api/v1/me/entitlements'))).data,
    enabled: computed(() => userId.value !== null),
    staleTime: 15 * 60_000,
    refetchOnWindowFocus: true,
  })
  return {
    query,
    entitlements: computed(() => (userId.value === null ? null : (query.data.value ?? null))),
  }
}

const day = 24 * 60 * 60 * 1000
const hour = 60 * 60 * 1000
/** setTimeout's longest delay (about 24.8 days). */
const maxDelay = 2 ** 31 - 1

/**
 * Whole days left of a reverse trial (a part of a day counts as one), or null without a trial, for
 * self-hosted instances and once it has ended.
 */
export function trialDaysLeft(
  entitlements: Pick<Entitlements, 'plan' | 'trialEndsAt'> | null,
  now = Date.now(),
): number | null {
  if (!entitlements?.trialEndsAt || entitlements.plan === 'selfhost') return null
  const end = Date.parse(entitlements.trialEndsAt)
  if (Number.isNaN(end) || end <= now) return null
  return Math.ceil((end - now) / day)
}

/**
 * `trialDaysLeft`, kept current while the page stays open: re-evaluated when the count drops (the
 * next whole day before the end, and the end itself) and at least hourly, in case a timer was held
 * back while the device slept. The timers end with the component (effect scope).
 */
export function useTrialDaysLeft(
  entitlements: Ref<Pick<Entitlements, 'plan' | 'trialEndsAt'> | null>,
) {
  const now = ref(Date.now())
  const tick = () => {
    now.value = Date.now()
  }
  const interval = setInterval(tick, hour)
  let next: ReturnType<typeof setTimeout> | undefined
  watch(
    [() => entitlements.value?.trialEndsAt, now],
    ([trialEndsAt, current]) => {
      clearTimeout(next)
      next = undefined
      const end = trialEndsAt ? Date.parse(trialEndsAt) : NaN
      if (!(end > current)) return
      // Until the remaining time crosses the next whole day (or reaches the end).
      const untilChange = (end - current) % day || day
      next = setTimeout(tick, Math.min(untilChange, maxDelay))
    },
    { immediate: true },
  )
  onScopeDispose(() => {
    clearInterval(interval)
    clearTimeout(next)
  })
  return computed(() => trialDaysLeft(entitlements.value, now.value))
}
